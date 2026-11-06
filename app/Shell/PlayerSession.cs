using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using WinSqueeze.Server;
using WinSqueeze.Settings;
namespace WinSqueeze.Shell;

// Live state of the selected player from Lyrion (polled ~1 s while visible, slower when hidden) and the commands the UI sends
public sealed class PlayerSession
{
    readonly DispatcherTimer pollTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    int pollTick;
    bool polling, pollAgain; // a poll asked for mid-poll runs straight after, so the state after the last command always arrives
    public string SelectedPlayerId { get; set; } = "";
    public string LastStatusJson { get; private set; } = "{}";
    public string LastPlayersJson { get; private set; } = "[]";
    public JsonElement LastStatus { get; private set; }
    public event Action? StatusUpdated;
    public event Action? PlayersUpdated;
    App App => App.Instance;
    string PlayerId => SelectedPlayerId == "" ? App.Settings.PlayerMac : SelectedPlayerId;

    public void Start() { pollTimer.Tick += async (sender, args) => await PollAsync(); pollTimer.Start(); }
    public void SetVisible(bool windowVisible) => pollTimer.Interval = TimeSpan.FromSeconds(windowVisible ? 1 : 5);

    async Task PollAsync()
    {
        if (polling) { pollAgain = true; return; }
        if (App.Settings.ActiveProfile is not { Host.Length: > 0 } profile) return;
        polling = true;
        try
        {
            using var lyrionClient = new LyrionClient(profile);
            LastStatus = await lyrionClient.RequestAsync(PlayerId, "status", 0, 500, "tags:aAcdlKjJotTIrsSeNfYm");
            LastStatusJson = LastStatus.ValueKind == JsonValueKind.Undefined ? "{}" : LastStatus.GetRawText();
            StatusUpdated?.Invoke();
            if (pollTick++ % 10 == 0)
            {
                var players = await lyrionClient.RequestAsync("", "players", 0, 50);
                LastPlayersJson = players.ValueKind == JsonValueKind.Object && players.TryGetProperty("players_loop", out var playersLoop) ? playersLoop.GetRawText() : "[]";
                PlayersUpdated?.Invoke();
            }
        }
        catch { }
        finally { polling = false; }
        if (pollAgain) { pollAgain = false; await PollAsync(); }
    }

    // Player commands go one at a time, so a fast volume drag can't land out of order (a late 98 overwriting the final 100)
    readonly SemaphoreSlim playerCommandGate = new(1, 1);
    public async Task<JsonElement> RequestAsync(bool forPlayer, params object[] command)
    {
        using var lyrionClient = new LyrionClient(App.Settings.ActiveProfile!);
        JsonElement result;
        if (forPlayer) { await playerCommandGate.WaitAsync(); try { result = await lyrionClient.RequestAsync(PlayerId, command); } finally { playerCommandGate.Release(); } }
        else result = await lyrionClient.RequestAsync("", command);
        if (forPlayer) { await Task.Delay(150); await PollAsync(); }
        return result;
    }

    // mode: load (replace queue), insert (play next), add (add to queue)
    public async Task PlayTracksAsync(List<long> trackIds, string mode, int startIndex)
    {
        if (trackIds.Count == 0) return;
        await RequestAsync(true, "playlistcontrol", $"cmd:{mode}", "track_id:" + string.Join(",", trackIds));
        if (mode == "load" && startIndex > 0) await RequestAsync(true, "playlist", "index", startIndex);
    }

    // Saves a Lyrion playlist (a deliberate, user-initiated write) and optionally favourites it
    public async Task<string> SavePlaylistAsync(string playlistName, List<string> trackUrls, bool addToFavourites, Action<int, int>? progress = null)
    {
        var created = await RequestAsync(false, "playlists", "new", $"name:{playlistName}");
        var playlistId = LyrionClient.ReadString(created, "playlist_id");
        if (playlistId == "") throw new InvalidOperationException(LyrionClient.ReadString(created, "overwritten_playlist_id") != "" ? "A playlist with that name already exists." : "The server did not create the playlist.");
        await AddToPlaylistAsync(playlistId, trackUrls, progress);
        if (addToFavourites)
        {
            var playlistInfo = await RequestAsync(false, "playlists", 0, 1, $"playlist_id:{playlistId}", "tags:u");
            if (playlistInfo.ValueKind == JsonValueKind.Object && playlistInfo.TryGetProperty("playlists_loop", out var playlistsLoop))
                foreach (var playlistItem in playlistsLoop.EnumerateArray()) await RequestAsync(false, "favorites", "add", $"url:{LyrionClient.ReadString(playlistItem, "url")}", $"title:{playlistName}");
        }
        return playlistId;
    }

    // Lyrion adds one track per request: one kept-alive connection for the lot (a new one each time made big playlists crawl), reporting progress
    public async Task AddToPlaylistAsync(string playlistId, List<string> trackUrls, Action<int, int>? progress = null)
    {
        using var lyrionClient = new LyrionClient(App.Settings.ActiveProfile!);
        for (var index = 0; index < trackUrls.Count; index++)
        {
            await lyrionClient.RequestAsync("", "playlists", "edit", "cmd:add", $"playlist_id:{playlistId}", $"url:{trackUrls[index]}");
            if (index % 5 == 4 || index == trackUrls.Count - 1) progress?.Invoke(index + 1, trackUrls.Count);
        }
    }

    public static string DraftPath => Path.Combine(AppPaths.DataFolder, "playlist-draft.json");
}
