using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WinSqueeze.Server;
using WinSqueeze.Settings;
using static WinSqueeze.Library.LibraryDatabase;
namespace WinSqueeze.Library;

// Every play, kept for good in history.db per server profile (SPEC.md › Play history). A play counts once it has run 20 seconds;
// library tracks are keyed by id, radio and other streams by URL, so a programme episode can be played again
public sealed class PlayHistory : IDisposable
{
    const int countAfterSeconds = 20;
    const string schema = "CREATE TABLE IF NOT EXISTS plays(id INTEGER PRIMARY KEY, played_at INTEGER, player TEXT, track_id INTEGER, url TEXT, title TEXT, artist TEXT, album TEXT, cover_id TEXT, artwork_url TEXT, station TEXT, remote INTEGER, duration REAL); CREATE INDEX IF NOT EXISTS plays_time ON plays(played_at);";
    readonly object stateLock = new();
    SqliteConnection? connection;
    string profileId = "", countedKey = "";
    double lastSeconds;
    public event Action? Changed;

    public void Open(ServerProfile? profile)
    {
        lock (stateLock)
        {
            if (profile?.Id == profileId && connection != null) return;
            connection?.Dispose(); connection = null; profileId = profile?.Id ?? ""; countedKey = "";
            if (profile == null) return;
            connection = LibraryDatabase.Open(Path.Combine(ProfileFolder(profile), "history.db"));
            Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;" + schema);
        }
    }

    // Called with every status poll
    public void OnStatus(JsonElement status)
    {
        if (status.ValueKind != JsonValueKind.Object || LyrionClient.ReadString(status, "mode") != "play" || Shell.MediaControls.CurrentTrack(status) is not { } track) return;
        var trackId = LyrionClient.ReadLong(track, "id"); var url = LyrionClient.ReadString(track, "url");
        var remote = trackId <= 0 || LyrionClient.ReadString(status, "remote") == "1";
        var key = remote ? "u" + (url.Length > 0 ? url : LyrionClient.ReadString(track, "title")) : "t" + trackId;
        var seconds = LyrionClient.ReadDouble(status, "time");
        if (key == countedKey && seconds < lastSeconds - 10) countedKey = ""; // the same track started again
        lastSeconds = seconds;
        if (key == countedKey || seconds < countAfterSeconds || connection == null) return;
        countedKey = key;
        lock (stateLock)
        {
            if (connection == null) return;
            Execute(connection, "INSERT INTO plays(played_at, player, track_id, url, title, artist, album, cover_id, artwork_url, station, remote, duration) VALUES($at, $player, $track, $url, $title, $artist, $album, $cover, $artwork, $station, $remote, $duration)",
                ("$at", DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)seconds), ("$player", LyrionClient.ReadString(status, "player_name")), ("$track", remote ? null : trackId), ("$url", url),
                ("$title", LyrionClient.ReadString(track, "title")), ("$artist", LyrionClient.ReadString(track, "artist")), ("$album", LyrionClient.ReadString(track, "album")), ("$cover", LyrionClient.ReadString(track, "coverid")),
                ("$artwork", LyrionClient.ReadString(track, "artwork_url")), ("$station", LyrionClient.ReadString(track, "remote_title")), ("$remote", remote ? 1 : 0), ("$duration", LyrionClient.ReadDouble(track, "duration")));
        }
        Changed?.Invoke();
    }

    // Newest first, a page at a time (before = played_at of the last row already shown)
    public List<Dictionary<string, object?>> Recent(long before, int limit)
    {
        lock (stateLock) return connection == null ? new() : Query(connection, "SELECT id, played_at, player, track_id, url, title, artist, album, cover_id, artwork_url, station, remote, duration FROM plays WHERE played_at < $before ORDER BY played_at DESC LIMIT $limit", ("$before", before <= 0 ? long.MaxValue : before), ("$limit", limit));
    }
    public void Delete(IEnumerable<long> ids) { lock (stateLock) if (connection != null) foreach (var playId in ids) Execute(connection, "DELETE FROM plays WHERE id=$id", ("$id", playId)); Changed?.Invoke(); }
    public void Clear() { lock (stateLock) if (connection != null) Execute(connection, "DELETE FROM plays"); Changed?.Invoke(); }
    public long Count() { lock (stateLock) return connection == null ? 0 : Convert.ToInt64(Query(connection, "SELECT COUNT(*) AS total FROM plays")[0]["total"]); }
    public void Dispose() { lock (stateLock) { connection?.Dispose(); connection = null; } }
}
