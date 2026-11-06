using System.IO;
using System.Text.Json;
using WinSqueeze.Server;
using WinSqueeze.Settings;
namespace WinSqueeze.Audio;

// Damaged files (SPEC.md › Damaged files): Squeezelite logs every decode error. Each error is tied to the track being decoded,
// warned about, skipped if it keeps happening, and remembered so lists can mark it and Settings can list it for restoring
public sealed class DamagedFiles
{
    public sealed class DamagedFile { public long Id { get; set; } public string Url { get; set; } = ""; public string Title { get; set; } = ""; public string Artist { get; set; } = ""; public string Album { get; set; } = ""; public int Errors { get; set; } public bool Unplayable { get; set; } public DateTime LastSeen { get; set; } public string Path => Uri.UnescapeDataString(Url.StartsWith("file://") ? Url["file://".Length..] : Url); }
    sealed class DecodeRecord { public bool PlayedNext; public List<DateTime> ErrorTimes = new(); public bool Fatal, Skipped, Announced; public Task<DamagedFile?>? Lookup; }

    public List<DamagedFile> Files { get; private set; } = new();
    public event Action<DamagedFile, string>? Found; // the file and what happened: "damaged", "skipped" or "unplayable"
    public event Action? Changed;
    public Func<object[], Task<JsonElement>> PlayerRequest = command => Task.FromResult(default(JsonElement)); // a command for this PC's own player
    const int SkipErrors = 3; static readonly TimeSpan SkipWindow = TimeSpan.FromSeconds(10);
    static string StorePath => System.IO.Path.Combine(AppPaths.DataFolder, "damaged-files.json");
    static readonly string[] errorMarkers = { "flac error:", "mad_frame_decode error", "ov_read error", "avformat_open_input:", "avformat_find_stream_info:" };

    // Squeezelite decodes ahead of what plays: "codec open" starts the next decode, "track start" is when the oldest waiting decode reaches the speakers
    readonly Dictionary<int, DecodeRecord> decodes = new();
    readonly Queue<int> waitingToPlay = new();
    int decodeCount, playingDecode;
    bool flushed; DateTime openedAt;

    public static bool Matters(string logLine) => logLine.Contains("codec open:") || logLine.Contains("track start") || logLine.Contains("flush output buffer (full)") || logLine.Contains("decode error") || errorMarkers.Any(logLine.Contains);

    public void Load() { try { Files = JsonSerializer.Deserialize<List<DamagedFile>>(File.ReadAllText(StorePath)) ?? new(); } catch { Files = new(); } }
    void Save() { try { File.WriteAllText(StorePath, JsonSerializer.Serialize(Files, new JsonSerializerOptions { WriteIndented = true })); } catch { } Changed?.Invoke(); }
    public void Clear() { Files = new(); Save(); }
    public void Reset() { decodes.Clear(); waitingToPlay.Clear(); playingDecode = 0; } // Squeezelite restarted

    // Called on the UI thread with lines that Matters()
    public void OnLogLine(string logLine)
    {
        var now = DateTime.Now;
        if (logLine.Contains("flush output buffer (full)")) { Reset(); flushed = true; return; } // stop, skip or seek: waiting decodes are thrown away
        if (logLine.Contains("codec open:")) { decodes[++decodeCount] = new DecodeRecord { PlayedNext = playingDecode != 0 }; waitingToPlay.Enqueue(decodeCount); openedAt = now; if (decodes.Count > 20) decodes.Remove(decodes.Keys.Min()); return; }
        if (logLine.Contains("track start"))
        {
            // A file the decoder rejected outright never reaches the speakers
            while (waitingToPlay.Count > 1 && decodes.TryGetValue(waitingToPlay.Peek(), out var rejected) && rejected.Fatal && rejected.ErrorTimes.Count == 0) waitingToPlay.Dequeue();
            if (waitingToPlay.Count > 0) { playingDecode = waitingToPlay.Dequeue(); _ = CheckAsync(playingDecode); }
            return;
        }
        if (!decodes.TryGetValue(decodeCount, out var record)) return;
        if (logLine.Contains("decode error") && !logLine.Contains("mad_frame")) record.Fatal = true; // the decoder gave up on the file
        else
        {
            // A seek starts mid-file: one error straight after it is the decoder finding its feet, not damage
            if (flushed && record.ErrorTimes.Count == 0 && now - openedAt < TimeSpan.FromMilliseconds(300)) { flushed = false; return; }
            record.ErrorTimes.Add(now);
        }
        flushed = false;
        _ = CheckAsync(decodeCount);
    }

    // Warn once per track, skip the track playing now if errors keep coming, remember it
    async Task CheckAsync(int decodeNumber)
    {
        if (!decodes.TryGetValue(decodeNumber, out var record) || (record.ErrorTimes.Count == 0 && !record.Fatal)) return;
        record.Lookup ??= LookupAsync(decodeNumber, record.PlayedNext);
        var damagedFile = await record.Lookup;
        if (damagedFile == null) return;
        var isNew = !Files.Contains(damagedFile); var becameUnplayable = record.Fatal && !damagedFile.Unplayable;
        damagedFile.Errors = Math.Max(damagedFile.Errors, record.ErrorTimes.Count); damagedFile.Unplayable |= record.Fatal; damagedFile.LastSeen = DateTime.Now;
        if (isNew) Files.Insert(0, damagedFile);
        if (isNew || becameUnplayable) Save(); // not on every error: a badly damaged file logs hundreds
        if (decodeNumber != playingDecode && !record.Fatal) return; // announced when it starts playing
        var recentErrors = record.ErrorTimes.Count(errorTime => DateTime.Now - errorTime < SkipWindow);
        if (decodeNumber == playingDecode && !record.Skipped && !record.Fatal && (recentErrors >= SkipErrors || (record.ErrorTimes.Count >= SkipErrors && !record.Announced)))
        {
            record.Skipped = record.Announced = true;
            try { await PlayerRequest(new object[] { "playlist", "index", "+1" }); } catch { }
            Save(); Found?.Invoke(damagedFile, "skipped");
        }
        else if (!record.Announced) { record.Announced = true; Save(); Found?.Invoke(damagedFile, record.Fatal ? "unplayable" : "damaged"); }
    }

    // Which track: the one playing, or (decoding ahead) the one after it. Only library files count, not radio or other streams
    async Task<DamagedFile?> LookupAsync(int decodeNumber, bool playedNext)
    {
        await Task.Delay(800); // the server catches up with a track that has just started
        var nextTrack = playedNext && decodeNumber != playingDecode;
        try
        {
            var status = await PlayerRequest(new object[] { "status", "-", 2, "tags:alu" });
            if (status.ValueKind != JsonValueKind.Object || !status.TryGetProperty("playlist_loop", out var playlistLoop)) return null;
            var items = playlistLoop.EnumerateArray().ToList();
            if (items.ElementAtOrDefault(nextTrack ? 1 : 0) is not { ValueKind: JsonValueKind.Object } item) return null;
            var url = LyrionClient.ReadString(item, "url");
            if (!url.StartsWith("file:")) return null;
            return Files.FirstOrDefault(known => known.Url == url) ?? new DamagedFile { Id = LyrionClient.ReadLong(item, "id"), Url = url, Title = LyrionClient.ReadString(item, "title"), Artist = LyrionClient.ReadString(item, "artist"), Album = LyrionClient.ReadString(item, "album") };
        }
        catch { return null; }
    }
}
