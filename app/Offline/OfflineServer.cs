using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
namespace WinSqueeze.Offline;

public sealed record OfflineTrack(long Id, string Path, string Title, string Artist, string Album, string CoverId, double Duration, string ContentType, int SampleRate, int SampleSize, int Year, int TrackNo, int DiscNo);

// A tiny stand-in for Lyrion on this PC (SPEC.md › Offline downloads): speaks slimproto to our Squeezelite, streams downloaded files to it over local HTTP,
// and answers the handful of player commands the UI sends (status, play/pause, seek, volume, queue edits) with Lyrion-shaped JSON
public sealed class OfflineServer : IDisposable
{
    readonly Func<long, OfflineTrack?> findTrack;
    readonly TcpListener slimListener = new(IPAddress.Loopback, 0), httpListener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource stopping = new();
    readonly object stateLock = new();
    readonly System.Threading.Timer statusTimer;
    public int SlimPort { get; }
    public int HttpPort { get; }
    public string PlayerMac { get; set; } = "";
    public string PlayerName { get; set; } = "This PC";
    public event Action? Changed;
    public event Action<string>? Log;

    // The queue holds entries (not bare ids), so the playing and pre-buffered tracks stay right when the queue is edited around them
    sealed class QueueEntry { public required OfflineTrack Track; }
    sealed record StreamPlan(OfflineTrack Track, long PrefixEnd, long StartOffset, double StartSeconds);
    readonly List<QueueEntry> queue = new();
    readonly Queue<(QueueEntry entry, double startSeconds)> streamsNotStarted = new(); // sent to Squeezelite, output not there yet
    readonly Dictionary<string, StreamPlan> streamPlans = new();
    QueueEntry? playingEntry, lastStreamed;
    string mode = "stop";
    double playingStartSeconds, elapsedAtStatus; long statusTick;
    int volume = 100, repeat, streamCounter;
    bool startPending; // a jump or seek was sent and its audio hasn't reached the output yet
    bool decoderIdle; // decoder finished but the track it decoded hasn't started playing: Squeezelite can mark only one upcoming track start, so the next waits
    TcpClient? playerClient; NetworkStream? playerStream;
    readonly object sendLock = new();

    public OfflineServer(Func<long, OfflineTrack?> findTrack)
    {
        this.findTrack = findTrack;
        slimListener.Start(); httpListener.Start();
        SlimPort = ((IPEndPoint)slimListener.LocalEndpoint).Port; HttpPort = ((IPEndPoint)httpListener.LocalEndpoint).Port;
        _ = Task.Run(AcceptPlayersAsync); _ = Task.Run(AcceptStreamsAsync);
        statusTimer = new System.Threading.Timer(_ => { if (mode == "play") Send("strm", Strm('t')); }, null, 1000, 1000); // asks Squeezelite for its position every second
    }

    public bool Connected => playerStream != null;
    public bool HasQueue { get { lock (stateLock) return queue.Count > 0; } }
    public bool IsPlaying => mode == "play";

    // ---- slimproto (player connection) ----
    async Task AcceptPlayersAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await slimListener.AcceptTcpClientAsync(stopping.Token); } catch { return; }
            client.NoDelay = true;
            lock (sendLock) { playerClient?.Dispose(); playerClient = client; playerStream = client.GetStream(); }
            _ = Task.Run(() => ReadPlayerAsync(client));
        }
    }

    async Task ReadPlayerAsync(TcpClient client)
    {
        var stream = client.GetStream(); var header = new byte[8];
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                await stream.ReadExactlyAsync(header, stopping.Token);
                var opcode = Encoding.ASCII.GetString(header, 0, 4); var length = (int)ReadUInt32(header, 4);
                var payload = new byte[length]; await stream.ReadExactlyAsync(payload, stopping.Token);
                OnPlayerMessage(opcode, payload);
            }
        }
        catch { }
        lock (sendLock) if (playerClient == client) { playerClient = null; playerStream = null; }
        client.Dispose(); Changed?.Invoke();
    }

    void OnPlayerMessage(string opcode, byte[] payload)
    {
        if (opcode == "HELO") { Send("aude", new byte[] { 1, 1 }); SendVolume(); Log?.Invoke("player connected"); Changed?.Invoke(); return; }
        if (opcode != "STAT" || payload.Length < 47) return;
        var statusEvent = Encoding.ASCII.GetString(payload, 0, 4);
        var elapsedMs = ReadUInt32(payload, 43);
        var notify = false;
        lock (stateLock)
        {
            switch (statusEvent)
            {
                case "STMs": // output reached the next stream: that track is now playing
                    if (streamsNotStarted.TryDequeue(out var started)) { playingEntry = started.entry; playingStartSeconds = started.startSeconds; }
                    if (decoderIdle && streamsNotStarted.Count == 0) { decoderIdle = false; if (NextAfter(lastStreamed) is { } waitingEntry) StartStream(waitingEntry, 0, false); }
                    startPending = false; elapsedAtStatus = 0; statusTick = Environment.TickCount64; notify = true; break;
                case "STMd": // decoder finished this stream: send the next track straight away so playback is gapless
                    if (streamsNotStarted.Count > 0) decoderIdle = true; else if (NextAfter(lastStreamed) is { } nextEntry) StartStream(nextEntry, 0, false);
                    break;
                case "STMu": // output ran dry with nothing after it: the queue has ended
                    if (streamsNotStarted.Count == 0) { mode = "stop"; notify = true; }
                    break;
                case "STMn": // format not supported: skip it
                    Log?.Invoke($"format not supported: {lastStreamed?.Track.Path}");
                    if (NextAfter(lastStreamed) is { } skipTo) { streamsNotStarted.Clear(); StartStream(skipTo, 0, true); } else mode = "stop";
                    notify = true; break;
                default:
                    if (statusEvent is "STMt" or "STMp" or "STMr" && !startPending) { elapsedAtStatus = elapsedMs / 1000.0; statusTick = Environment.TickCount64; }
                    break;
            }
        }
        if (notify) Changed?.Invoke();
    }

    QueueEntry? NextAfter(QueueEntry? entry)
    {
        var position = entry == null ? -1 : queue.IndexOf(entry);
        if (position < 0 && entry != null) return null; // it was removed from the queue
        if (position + 1 < queue.Count) return queue[position + 1];
        return repeat > 0 && queue.Count > 0 ? queue[0] : null;
    }

    // flush: drop what's playing now (a jump or seek); otherwise the stream is queued behind the current one (gapless)
    void StartStream(QueueEntry entry, double seconds, bool flush)
    {
        if (flush) { Send("strm", Strm('q')); streamsNotStarted.Clear(); decoderIdle = false; }
        var plan = PlanStream(entry.Track, seconds);
        var token = $"{++streamCounter}";
        streamPlans[token] = plan;
        streamPlans.Remove($"{streamCounter - 8}");
        streamsNotStarted.Enqueue((entry, plan.StartSeconds)); lastStreamed = entry;
        if (flush) { playingEntry = entry; playingStartSeconds = plan.StartSeconds; elapsedAtStatus = 0; statusTick = Environment.TickCount64; mode = "play"; startPending = true; }
        var packet = Strm('s', FormatCode(entry.Track.ContentType), $"GET /stream/{token} HTTP/1.0\r\n\r\n");
        if (packet[2] == 'p' && AudioSeek.PcmFormat(entry.Track.Path) is { } pcmFormat) pcmFormat.CopyTo(packet, 3); // PCM needs size, rate, channels and byte order spelled out
        Send("strm", packet);
    }

    // Squeezelite's codec letters for Lyrion content types
    static char FormatCode(string contentType) => contentType.ToLowerInvariant() switch { "flc" or "flac" => 'f', "mp3" => 'm', "wav" or "aif" or "aiff" or "pcm" => 'p', "ogg" => 'o', "ops" => 'u', "alc" or "alac" => 'l', "aac" or "mp4" or "m4a" => 'a', "dsf" or "dff" => 'd', _ => 'f' };

    static byte[] Strm(char command, char format = '?', string request = "")
    {
        var requestBytes = Encoding.ASCII.GetBytes(request);
        var packet = new byte[24 + requestBytes.Length];
        packet[0] = (byte)command; packet[1] = (byte)'1'; packet[2] = (byte)format; // autostart once the buffer threshold is reached
        packet[3] = packet[4] = packet[5] = packet[6] = (byte)'?'; // size, rate, channels, endianness come from the file itself
        packet[7] = 20; packet[8] = (byte)'0'; packet[10] = (byte)'0'; // threshold (KB), no S/PDIF, no crossfade
        // replay gain 0; server ip 0 = the slimproto server's own address
        if (command == 't') WriteUInt32(packet, 14, (uint)Environment.TickCount);
        packet[18] = 0; packet[19] = 0; // port filled in by Send for 's'
        requestBytes.CopyTo(packet, 24);
        return packet;
    }

    void Send(string opcode, byte[] payload)
    {
        if (opcode == "strm" && payload[0] == 's') { payload[18] = (byte)(HttpPort >> 8); payload[19] = (byte)HttpPort; }
        var frame = new byte[6 + payload.Length];
        frame[0] = (byte)((payload.Length + 4) >> 8); frame[1] = (byte)(payload.Length + 4);
        Encoding.ASCII.GetBytes(opcode).CopyTo(frame, 2); payload.CopyTo(frame, 6);
        lock (sendLock) { try { playerStream?.Write(frame); } catch { } }
    }

    // Lyrion-like volume curve: 100 is unity (bit-perfect), each step down is half a dB, 0 is silence
    void SendVolume()
    {
        var gain = volume <= 0 ? 0u : (uint)Math.Round(65536 * Math.Pow(10, (volume - 100) * 0.5 / 20));
        var packet = new byte[18];
        packet[8] = 1; packet[9] = 255; // adjust, preamp
        WriteUInt32(packet, 10, gain); WriteUInt32(packet, 14, gain);
        Send("audg", packet);
    }

    // ---- HTTP (audio to Squeezelite) ----
    async Task AcceptStreamsAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await httpListener.AcceptTcpClientAsync(stopping.Token); } catch { return; }
            _ = Task.Run(() => ServeStreamAsync(client));
        }
    }

    async Task ServeStreamAsync(TcpClient client)
    {
        using var connection = client; var stream = client.GetStream();
        try
        {
            var requestText = new StringBuilder(); var oneByte = new byte[1];
            while (!requestText.ToString().EndsWith("\r\n\r\n") && requestText.Length < 4096 && await stream.ReadAsync(oneByte, stopping.Token) == 1) requestText.Append((char)oneByte[0]);
            var token = requestText.ToString().Split(' ').ElementAtOrDefault(1)?.Split('/').LastOrDefault() ?? "";
            StreamPlan? plan; lock (stateLock) streamPlans.TryGetValue(token, out plan);
            if (plan == null || !File.Exists(plan.Track.Path)) { await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.0 404 Not Found\r\n\r\n")); return; }
            using var file = new FileStream(plan.Track.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, true);
            var length = plan.PrefixEnd + (file.Length - plan.StartOffset);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.0 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: {length}\r\nConnection: close\r\n\r\n"));
            var buffer = new byte[1 << 16];
            async Task CopyRangeAsync(long from, long to) { file.Position = from; while (file.Position < to) { var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, to - file.Position)), stopping.Token); if (read <= 0) break; await stream.WriteAsync(buffer.AsMemory(0, read), stopping.Token); } }
            if (plan.PrefixEnd > 0) await CopyRangeAsync(0, plan.PrefixEnd); // the file's own header, so the decoder knows the format after a seek
            await CopyRangeAsync(plan.StartOffset, file.Length);
        }
        catch { } // Squeezelite closes the connection on a skip or seek
    }

    // Where to start reading for a seek: frame-exact for FLAC (seek table or a validated frame header), sample-exact for WAV/AIFF, approximate for MP3; other formats play from the start
    static StreamPlan PlanStream(OfflineTrack track, double seconds)
    {
        if (seconds <= 0 || !File.Exists(track.Path)) return new(track, 0, 0, 0);
        try
        {
            using var file = File.OpenRead(track.Path);
            return track.ContentType.ToLowerInvariant() switch
            {
                "flc" or "flac" => AudioSeek.Flac(file, track, seconds),
                "wav" => AudioSeek.Wav(file, track, seconds),
                "aif" or "aiff" => AudioSeek.Aiff(file, track, seconds),
                "mp3" => AudioSeek.Mp3(file, track, seconds),
                _ => null
            } is var (prefixEnd, startOffset, startSeconds) ? new(track, prefixEnd, startOffset, startSeconds) : new(track, 0, 0, 0);
        }
        catch { return new(track, 0, 0, 0); }
    }

    // ---- Lyrion-shaped commands from the UI ----
    public JsonElement Request(object[] command)
    {
        var words = command.Select(part => Convert.ToString(part, System.Globalization.CultureInfo.InvariantCulture)?.Trim('"') ?? "").ToArray();
        var result = new Dictionary<string, object?>();
        lock (stateLock)
        {
            switch (words.ElementAtOrDefault(0))
            {
                case "status": return JsonSerializer.SerializeToElement(Status());
                case "players": result["count"] = 1; result["players_loop"] = new[] { PlayerRow() }; break;
                case "serverstatus": result["version"] = "offline"; result["player count"] = 1; break;
                case "connected": result["_connected"] = Connected ? 1 : 0; break;
                case "play": if (mode == "pause") Unpause(); else if (mode == "stop" && (playingEntry ?? queue.FirstOrDefault()) is { } toPlay) StartStream(toPlay, 0, true); break;
                case "stop": Send("strm", Strm('q')); streamsNotStarted.Clear(); mode = "stop"; break;
                case "pause":
                    var wantPause = words.Length > 1 && words[1] != "" ? words[1] == "1" : mode == "play";
                    if (wantPause && mode == "play") { Send("strm", Strm('p')); elapsedAtStatus = CurrentTime() - playingStartSeconds; statusTick = Environment.TickCount64; mode = "pause"; }
                    else if (!wantPause && mode == "pause") Unpause();
                    else if (!wantPause && mode == "stop" && (playingEntry ?? queue.FirstOrDefault()) is { } resumeEntry) StartStream(resumeEntry, 0, true);
                    break;
                case "time":
                    if (words.ElementAtOrDefault(1) == "?") { result["_time"] = CurrentTime(); break; }
                    if (playingEntry != null && double.TryParse(words.ElementAtOrDefault(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seekSeconds)) StartStream(playingEntry, Math.Clamp(seekSeconds, 0, Math.Max(0, playingEntry.Track.Duration - 1)), true);
                    break;
                case "mixer":
                    if (words.ElementAtOrDefault(1) != "volume") break;
                    if (words.ElementAtOrDefault(2) == "?") { result["_volume"] = volume; break; }
                    var volumeText = words.ElementAtOrDefault(2) ?? "";
                    if (int.TryParse(volumeText.TrimStart('+'), out var volumeValue)) { volume = Math.Clamp(volumeText.StartsWith('+') || volumeText.StartsWith('-') ? volume + volumeValue : volumeValue, 0, 100); SendVolume(); }
                    break;
                case "playlist": Playlist(words.Skip(1).ToArray()); break;
                case "playlistcontrol":
                    var commandName = words.FirstOrDefault(word => word.StartsWith("cmd:"))?[4..] ?? "";
                    var trackIds = (words.FirstOrDefault(word => word.StartsWith("track_id:"))?[9..] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => long.TryParse(id, out var trackId) ? trackId : 0);
                    AddTracks(trackIds.Select(findTrack).Where(track => track != null && File.Exists(track.Path)).Select(track => new QueueEntry { Track = track! }).ToList(), commandName);
                    result["count"] = trackIds.Count(); break;
            }
        }
        Changed?.Invoke();
        return JsonSerializer.SerializeToElement(result);
    }

    void Unpause() { Send("strm", Strm('u')); statusTick = Environment.TickCount64; mode = "play"; }

    void AddTracks(List<QueueEntry> entries, string commandName)
    {
        if (commandName == "load") { queue.Clear(); queue.AddRange(entries); if (entries.Count > 0) StartStream(entries[0], 0, true); else { Send("strm", Strm('q')); mode = "stop"; playingEntry = null; } return; }
        var insertAt = commandName == "insert" && playingEntry != null ? queue.IndexOf(playingEntry) + 1 : queue.Count;
        queue.InsertRange(Math.Clamp(insertAt, 0, queue.Count), entries);
        playingEntry ??= queue.FirstOrDefault();
        RebufferIfNextChanged();
    }

    void Playlist(string[] words)
    {
        int Number(int position) => int.TryParse(words.ElementAtOrDefault(position), out var number) ? number : -1;
        var playingPosition = playingEntry == null ? -1 : queue.IndexOf(playingEntry);
        switch (words.ElementAtOrDefault(0))
        {
            case "index":
                var indexText = words.ElementAtOrDefault(1) ?? "";
                var target = indexText.StartsWith('+') || indexText.StartsWith('-') ? playingPosition + int.Parse(indexText) : Number(1);
                if (repeat > 0 && queue.Count > 0) target = (target % queue.Count + queue.Count) % queue.Count;
                if (target >= 0 && target < queue.Count) StartStream(queue[target], 0, true);
                break;
            case "delete":
                var deleteAt = Number(1);
                if (deleteAt < 0 || deleteAt >= queue.Count) break;
                var removed = queue[deleteAt]; queue.RemoveAt(deleteAt);
                if (removed == playingEntry) { if (deleteAt < queue.Count && mode != "stop") StartStream(queue[deleteAt], 0, true); else { if (mode != "stop") Send("strm", Strm('q')); mode = "stop"; playingEntry = deleteAt < queue.Count ? queue[deleteAt] : queue.LastOrDefault(); } }
                else RebufferIfNextChanged();
                break;
            case "move":
                int from = Number(1), to = Number(2);
                if (from < 0 || from >= queue.Count || to < 0 || to >= queue.Count) break;
                var moved = queue[from]; queue.RemoveAt(from); queue.Insert(to, moved);
                RebufferIfNextChanged(); break;
            case "clear": queue.Clear(); Send("strm", Strm('q')); streamsNotStarted.Clear(); mode = "stop"; playingEntry = null; break;
            case "repeat": if (words.ElementAtOrDefault(1) is { } repeatText && int.TryParse(repeatText, out var repeatMode)) repeat = repeatMode; break;
        }
    }

    // The next track may already be buffered; if the queue edit changed what comes next, restart the current one at the same spot so the right one follows
    void RebufferIfNextChanged()
    {
        if (mode != "play" || playingEntry == null || streamsNotStarted.Count == 0) return;
        var buffered = streamsNotStarted.Last().entry;
        if (buffered == NextAfter(playingEntry)) return;
        StartStream(playingEntry, CurrentTime(), true);
    }

    double CurrentTime()
    {
        var running = mode == "play" && !startPending ? (Environment.TickCount64 - statusTick) / 1000.0 : 0;
        return Math.Min(playingStartSeconds + elapsedAtStatus + running, Math.Max(0, playingEntry?.Track.Duration ?? 0));
    }

    object PlayerRow() => new Dictionary<string, object?> { ["playerid"] = PlayerMac, ["name"] = PlayerName, ["connected"] = Connected ? 1 : 0, ["isplaying"] = mode == "play" ? 1 : 0, ["model"] = "squeezelite", ["offline"] = 1 };

    Dictionary<string, object?> Status()
    {
        var playingPosition = playingEntry == null ? 0 : Math.Max(0, queue.IndexOf(playingEntry));
        var status = new Dictionary<string, object?>
        {
            ["player_name"] = PlayerName, ["player_connected"] = Connected ? 1 : 0, ["power"] = 1, ["mode"] = mode, ["time"] = CurrentTime(), ["rate"] = 1, ["can_seek"] = 1,
            ["mixer volume"] = volume, ["playlist repeat"] = repeat, ["playlist shuffle"] = 0, ["playlist mode"] = "off", ["playlist_tracks"] = queue.Count, ["remote"] = 0, ["offline"] = 1
        };
        if (queue.Count > 0) { status["playlist_cur_index"] = playingPosition.ToString(); status["duration"] = queue[playingPosition].Track.Duration; }
        status["playlist_loop"] = queue.Select((entry, position) => new Dictionary<string, object?>
        {
            ["playlist index"] = position, ["id"] = entry.Track.Id, ["title"] = entry.Track.Title, ["artist"] = entry.Track.Artist, ["album"] = entry.Track.Album, ["coverid"] = entry.Track.CoverId, ["duration"] = entry.Track.Duration,
            ["type"] = entry.Track.ContentType, ["samplerate"] = entry.Track.SampleRate, ["samplesize"] = entry.Track.SampleSize, ["year"] = entry.Track.Year, ["tracknum"] = entry.Track.TrackNo, ["disc"] = entry.Track.DiscNo
        }).ToList();
        return status;
    }

    // Queue and position, so going offline (or back online) can carry on with the same tracks
    public (List<long> ids, int index, double seconds, bool playing) Snapshot() { lock (stateLock) return (queue.Select(entry => entry.Track.Id).ToList(), playingEntry == null ? 0 : Math.Max(0, queue.IndexOf(playingEntry)), CurrentTime(), mode == "play"); }

    static uint ReadUInt32(byte[] bytes, int offset) => (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);
    static void WriteUInt32(byte[] bytes, int offset, uint value) { bytes[offset] = (byte)(value >> 24); bytes[offset + 1] = (byte)(value >> 16); bytes[offset + 2] = (byte)(value >> 8); bytes[offset + 3] = (byte)value; }

    public void Dispose()
    {
        stopping.Cancel(); statusTimer.Dispose();
        try { slimListener.Stop(); httpListener.Stop(); } catch { }
        lock (sendLock) { playerClient?.Dispose(); playerClient = null; playerStream = null; }
    }
}
