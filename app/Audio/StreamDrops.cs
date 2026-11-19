using System.IO;
using System.Text.Json;
using WinSqueeze.Server;
using WinSqueeze.Settings;
namespace WinSqueeze.Audio;

// Dropped streams (SPEC.md › Dropped streams): Squeezelite treats a broken connection to the server as the end of the file, plays what it
// has buffered and moves on, so the rest of the track is silently skipped. Typical cause: a track paused for a long time while the PC slept
// or the network changed. When the playing track's stream drops, WinSqueeze seeks the server to where it is, which opens a fresh stream
public sealed class StreamDrops
{
    public Func<object[], Task<JsonElement>> PlayerRequest = command => Task.FromResult(default(JsonElement)); // a command for this PC's own player
    const int maxRecoveriesPerTrack = 3;

    // Squeezelite opens the next file's stream while the current one plays out: "codec open" starts a stream, "track start" is when it reaches the speakers
    bool newestStreamPlaying;
    int recoveriesThisTrack;

    public static bool Matters(string logLine) => logLine.Contains("codec open:") || logLine.Contains("track start") || logLine.Contains("flush output buffer (full)") || logLine.Contains("error reading");

    public void Reset() { newestStreamPlaying = false; recoveriesThisTrack = 0; } // Squeezelite restarted

    // Called on the UI thread with lines that Matters()
    public void OnLogLine(string logLine)
    {
        if (logLine.Contains("flush output buffer (full)")) { newestStreamPlaying = false; return; } // stop, skip or seek: a new stream follows
        if (logLine.Contains("codec open:")) { newestStreamPlaying = false; return; }
        if (logLine.Contains("track start")) { if (!newestStreamPlaying) recoveriesThisTrack = 0; newestStreamPlaying = true; return; }
        if (logLine.Contains("error reading")) _ = OnDroppedAsync(Reason(logLine), newestStreamPlaying);
    }

    // "stream_thread:716 error reading: Unknown error (10054)" → "error 10054, the connection was reset"
    static string Reason(string logLine)
    {
        var detail = logLine[(logLine.IndexOf("error reading") + "error reading".Length)..].Trim(' ', ':');
        var code = System.Text.RegularExpressions.Regex.Match(detail, @"\((\d+)\)").Groups[1].Value;
        var meaning = code switch { "10054" => "the connection was reset", "10053" => "this PC dropped the connection", "10060" => "the server stopped answering", "10065" or "10051" => "the server couldn't be reached", _ => "" };
        return code == "" ? detail : $"error {code}{(meaning == "" ? "" : $", {meaning}")}";
    }

    async Task OnDroppedAsync(string reason, bool playingTrack)
    {
        var (title, elapsed, duration, mode) = await ReadNowPlayingAsync();
        var where = duration > 0 ? $" at {Clock(elapsed)} of {Clock(duration)}" : "";
        if (!playingTrack) { Log($"Stream dropped while loading the next track, which may end early ({reason})"); return; } // the next track hasn't started, so there's nothing to seek yet
        if (mode != "play") { Log($"Stream dropped{where} while {(mode == "" ? "the server wasn't answering" : mode == "pause" ? "paused" : "stopped")}: {title} ({reason})"); return; }
        if (duration > 0 && duration - elapsed < 3) { Log($"Stream dropped at the very end of {title} ({reason})"); return; }
        if (++recoveriesThisTrack > maxRecoveriesPerTrack) { Log($"Stream dropped again{where}: {title} ({reason}). Gave up reconnecting; the rest of the track will be skipped"); return; }
        try
        {
            await PlayerRequest(new object[] { "time", Math.Round(elapsed, 1) });
            Log($"Stream dropped{where}: {title} ({reason}). Reconnected and carried on from {Clock(elapsed)}");
        }
        catch (Exception error) { Log($"Stream dropped{where}: {title} ({reason}). Couldn't reconnect ({error.Message}); the rest of the track will be skipped"); }
    }

    async Task<(string title, double elapsed, double duration, string mode)> ReadNowPlayingAsync()
    {
        try
        {
            var status = await PlayerRequest(new object[] { "status", "-", 1, "tags:al" });
            if (status.ValueKind != JsonValueKind.Object) return ("", 0, 0, "");
            var title = "";
            if (status.TryGetProperty("playlist_loop", out var playlistLoop) && playlistLoop.ValueKind == JsonValueKind.Array && playlistLoop.GetArrayLength() > 0)
            {
                var item = playlistLoop[0]; var artist = LyrionClient.ReadString(item, "artist");
                title = (artist == "" ? "" : artist + " – ") + LyrionClient.ReadString(item, "title");
            }
            return (title, LyrionClient.ReadDouble(status, "time"), LyrionClient.ReadDouble(status, "duration"), LyrionClient.ReadString(status, "mode"));
        }
        catch { return ("", 0, 0, ""); }
    }

    static string Clock(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    // logs\playback.log: dropped streams and what was done about them. Kept to its last 300 lines
    static readonly object logLock = new();
    public static void Log(string line)
    {
        try
        {
            lock (logLock)
            {
                var logPath = Path.Combine(AppPaths.LogsFolder, "playback.log");
                var keptLines = File.Exists(logPath) ? File.ReadAllLines(logPath).TakeLast(299).ToList() : new List<string>();
                keptLines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}");
                File.WriteAllLines(logPath, keptLines);
            }
        }
        catch { }
    }
}
