using System.Text.Json;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using WinSqueeze.Server;
namespace WinSqueeze.Shell;

// System Media Transport Controls: media keys, the volume flyout and lock screen (SPEC.md › Media integration)
public sealed class MediaControls
{
    readonly MediaPlayer hostPlayer = new();
    readonly SystemMediaTransportControls transportControls;
    string shownTrackKey = "";
    App App => App.Instance;

    public MediaControls()
    {
        hostPlayer.CommandManager.IsEnabled = false; // we drive SMTC ourselves; no local playback
        transportControls = hostPlayer.SystemMediaTransportControls;
        transportControls.IsEnabled = true;
        transportControls.IsPlayEnabled = true; transportControls.IsPauseEnabled = true; transportControls.IsNextEnabled = true; transportControls.IsPreviousEnabled = true;
        transportControls.ButtonPressed += (sender, args) => App.Dispatcher.BeginInvoke(async () =>
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play: await App.Session.RequestAsync(true, "pause", "0"); break;
                case SystemMediaTransportControlsButton.Pause: await App.Session.RequestAsync(true, "pause", "1"); break;
                case SystemMediaTransportControlsButton.Next: await App.Session.RequestAsync(true, "playlist", "index", "+1"); break;
                case SystemMediaTransportControlsButton.Previous: await App.Session.RequestAsync(true, "playlist", "index", "-1"); break;
            }
        });
    }

    public void Update(JsonElement status, string coverUrlBase)
    {
        if (status.ValueKind != JsonValueKind.Object) return;
        var mode = LyrionClient.ReadString(status, "mode");
        transportControls.PlaybackStatus = mode switch { "play" => MediaPlaybackStatus.Playing, "pause" => MediaPlaybackStatus.Paused, _ => MediaPlaybackStatus.Stopped };
        var currentTrack = CurrentTrack(status);
        if (currentTrack == null) return;
        var trackKey = LyrionClient.ReadString(currentTrack.Value, "id") + LyrionClient.ReadString(currentTrack.Value, "title");
        if (trackKey == shownTrackKey) return;
        shownTrackKey = trackKey;
        var displayUpdater = transportControls.DisplayUpdater;
        displayUpdater.Type = MediaPlaybackType.Music;
        displayUpdater.MusicProperties.Title = LyrionClient.ReadString(currentTrack.Value, "title");
        displayUpdater.MusicProperties.Artist = LyrionClient.ReadString(currentTrack.Value, "artist");
        displayUpdater.MusicProperties.AlbumTitle = LyrionClient.ReadString(currentTrack.Value, "album");
        var coverId = LyrionClient.ReadString(currentTrack.Value, "coverid");
        displayUpdater.Thumbnail = coverId == "" ? null : RandomAccessStreamReference.CreateFromUri(new Uri($"{coverUrlBase}/music/{coverId}/cover_300x300_o"));
        displayUpdater.Update();
    }

    public static JsonElement? CurrentTrack(JsonElement status)
    {
        if (!status.TryGetProperty("playlist_loop", out var playlistLoop)) return null;
        var currentIndex = LyrionClient.ReadString(status, "playlist_cur_index");
        foreach (var queueItem in playlistLoop.EnumerateArray()) if (LyrionClient.ReadString(queueItem, "playlist index") == currentIndex) return queueItem;
        return null;
    }
}
