using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WinSqueeze.Library;
using WinSqueeze.Settings;
using static WinSqueeze.Library.LibraryDatabase;
namespace WinSqueeze.Offline;

// Downloads original files from Lyrion for offline play (SPEC.md › Offline downloads). One downloads.db per server profile records every track's
// state; files live under <data>\Offline\<profile>\<album artist>\<album>\. Two at a time, resumable (HTTP Range), survives restarts
public sealed class DownloadManager : IDisposable
{
    const int parallelDownloads = 2;
    static readonly int[] artSizes = { 80, 120, 300, 600 };
    readonly object stateLock = new();
    readonly Dictionary<long, (CancellationTokenSource cancel, long received, long size)> active = new();
    SqliteConnection? connection;
    ServerProfile? profile;
    DateTime pausedUntil = DateTime.MinValue;
    System.Threading.Timer? changeTimer;
    bool changeQueued;
    public event Action? Changed; // throttled to a few a second
    public Func<string, int, Task>? CacheArt; // coverId, size
    public string LastProblem { get; private set; } = "";

    public static string RootFolder => Path.Combine(AppPaths.DataFolder, "Offline");
    public string Folder => profile == null ? RootFolder : Path.Combine(RootFolder, profile.Id);

    const string schema = "CREATE TABLE IF NOT EXISTS downloads(track_id INTEGER PRIMARY KEY, album_key TEXT, album_title TEXT, album_artist TEXT, cover_id TEXT, title TEXT, artist TEXT, disc_no INTEGER, track_no INTEGER, duration REAL, content_type TEXT, sample_rate INTEGER, sample_size INTEGER, year INTEGER, url TEXT, size INTEGER DEFAULT 0, received INTEGER DEFAULT 0, state TEXT, error TEXT DEFAULT '', path TEXT, position INTEGER);";

    // Switching server profile switches the download list
    public void Open(ServerProfile? newProfile)
    {
        lock (stateLock)
        {
            if (newProfile?.Id == profile?.Id && connection != null) return;
            foreach (var download in active.Values) download.cancel.Cancel();
            active.Clear(); connection?.Dispose(); connection = null; profile = newProfile;
            if (profile == null) return;
            connection = LibraryDatabase.Open(Path.Combine(ProfileFolder(profile), "downloads.db"));
            Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;" + schema);
            Execute(connection, "UPDATE downloads SET state='queued' WHERE state='downloading'"); // interrupted last time: carry on
        }
        changeTimer ??= new System.Threading.Timer(_ => { if (changeQueued) { changeQueued = false; Changed?.Invoke(); } }, null, 300, 300);
        Pump();
    }

    List<Dictionary<string, object?>> Rows(string sql, params (string, object?)[] parameters) { lock (stateLock) return connection == null ? new() : Query(connection, sql, parameters); }
    void Run(string sql, params (string, object?)[] parameters) { lock (stateLock) if (connection != null) Execute(connection, sql, parameters); }
    void NotifyChanged() => changeQueued = true;

    // Adds tracks (metadata from the library index); already-downloaded ones are left alone, failed ones are retried
    public int Enqueue(List<Dictionary<string, object?>> tracks)
    {
        var added = 0;
        lock (stateLock)
        {
            if (connection == null) return 0;
            var nextPosition = Convert.ToInt64(Query(connection, "SELECT COALESCE(MAX(position), 0) + 1 AS next FROM downloads")[0]["next"]);
            using var transaction = connection.BeginTransaction();
            foreach (var track in tracks)
            {
                var existing = Query(connection, "SELECT state FROM downloads WHERE track_id=$id", ("$id", track["id"])).FirstOrDefault()?["state"] as string;
                if (existing is "done" or "queued" or "downloading") continue;
                string Text(string name) => Convert.ToString(track.GetValueOrDefault(name)) ?? "";
                var albumFolder = Path.Combine(Folder, SafeName(Text("album_artist"), "Unknown artist"), SafeName(Text("album_title"), "Unknown album"));
                var fileName = SafeName($"{(Convert.ToInt32(track.GetValueOrDefault("disc_no") ?? 0) > 1 ? Text("disc_no") + "-" : "")}{Convert.ToInt32(track.GetValueOrDefault("track_no") ?? 0):00} {Text("title")}", Text("id")) + Extension(Text("url"), Text("content_type"));
                Execute(connection, "INSERT OR REPLACE INTO downloads(track_id, album_key, album_title, album_artist, cover_id, title, artist, disc_no, track_no, duration, content_type, sample_rate, sample_size, year, url, size, received, state, error, path, position) VALUES($id, $album_key, $album_title, $album_artist, $cover_id, $title, $artist, $disc_no, $track_no, $duration, $content_type, $sample_rate, $sample_size, $year, $url, 0, 0, 'queued', '', $path, $position)",
                    ("$id", track["id"]), ("$album_key", Text("album_key")), ("$album_title", Text("album_title")), ("$album_artist", Text("album_artist")), ("$cover_id", Text("cover_id")), ("$title", Text("title")), ("$artist", Text("artist")), ("$disc_no", track.GetValueOrDefault("disc_no")), ("$track_no", track.GetValueOrDefault("track_no")), ("$duration", track.GetValueOrDefault("duration")),
                    ("$content_type", Text("content_type")), ("$sample_rate", track.GetValueOrDefault("sample_rate")), ("$sample_size", track.GetValueOrDefault("sample_size")), ("$year", track.GetValueOrDefault("year")), ("$url", Text("url")), ("$path", Path.Combine(albumFolder, fileName)), ("$position", nextPosition++));
                added++;
            }
            transaction.Commit();
        }
        pausedUntil = DateTime.MinValue;
        NotifyChanged(); Pump();
        return added;
    }

    static string SafeName(string name, string fallback)
    {
        var cleaned = new string(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) || character < ' ' ? '_' : character).ToArray()).Trim().TrimEnd('.');
        return cleaned == "" ? fallback : cleaned.Length > 80 ? cleaned[..80].Trim() : cleaned;
    }
    static string Extension(string url, string contentType)
    {
        try { var extension = Path.GetExtension(Uri.UnescapeDataString(new Uri(url).AbsolutePath)); if (extension.Length is > 1 and < 6) return extension.ToLowerInvariant(); } catch { }
        return "." + (contentType switch { "flc" => "flac", "alc" or "aac" => "m4a", "aif" => "aiff", "" => "bin", _ => contentType });
    }

    // Starts queued downloads up to the limit; pauses for a while when the server can't be reached
    void Pump()
    {
        if (DateTime.Now < pausedUntil || profile is not { Host.Length: > 0 }) return;
        List<Dictionary<string, object?>> next;
        lock (stateLock)
        {
            if (connection == null || active.Count >= parallelDownloads) return;
            next = Query(connection, $"SELECT * FROM downloads WHERE state='queued' AND track_id NOT IN (SELECT value FROM json_each($active)) ORDER BY position LIMIT {parallelDownloads - active.Count}", ("$active", JsonSerializer.Serialize(active.Keys)));
            foreach (var row in next) { var trackId = Convert.ToInt64(row["track_id"]); active[trackId] = (new CancellationTokenSource(), Convert.ToInt64(row["received"]), Convert.ToInt64(row["size"])); Execute(connection, "UPDATE downloads SET state='downloading', error='' WHERE track_id=$id", ("$id", trackId)); }
        }
        foreach (var row in next) _ = Task.Run(() => DownloadAsync(row, profile));
    }

    async Task DownloadAsync(Dictionary<string, object?> row, ServerProfile serverProfile)
    {
        var trackId = Convert.ToInt64(row["track_id"]); var finalPath = (string)row["path"]!; var partPath = finalPath + ".part";
        CancellationToken cancelToken; lock (stateLock) { if (!active.TryGetValue(trackId, out var entry)) return; cancelToken = entry.cancel.Token; }
        var outcome = "done"; var error = "";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            if (serverProfile.Username != "") httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{serverProfile.Username}:{serverProfile.Password}")));
            var already = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://{serverProfile.Host}:{serverProfile.Port}/music/{trackId}/download");
            if (already > 0) request.Headers.Range = new RangeHeaderValue(already, null);
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancelToken); connectTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connectTimeout.Token);
            if (!response.IsSuccessStatusCode) { outcome = "failed"; error = L.T("The server answered {status}", ("status", $"{(int)response.StatusCode} {response.ReasonPhrase}")); return; }
            var resuming = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (!resuming) already = 0; // the server sent the whole file again
            var totalSize = already + (response.Content.Headers.ContentLength ?? 0);
            using (var file = new FileStream(partPath, resuming ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16, true))
            await using (var body = await response.Content.ReadAsStreamAsync(cancelToken))
            {
                var buffer = new byte[1 << 16]; var received = already;
                while (true)
                {
                    // A stalled transfer (VPN dropped) gives up after 30 s and is retried later from where it stopped
                    using var stallTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancelToken); stallTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                    var read = await body.ReadAsync(buffer, stallTimeout.Token);
                    if (read == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, read), cancelToken);
                    received += read;
                    lock (stateLock) if (active.ContainsKey(trackId)) active[trackId] = (active[trackId].cancel, received, totalSize);
                    NotifyChanged();
                }
                if (totalSize > already && received < totalSize) throw new IOException(L.T("The download stopped early"));
            }
            File.Move(partPath, finalPath, true);
            Run("UPDATE downloads SET size=$size, received=$size WHERE track_id=$id", ("$size", new FileInfo(finalPath).Length), ("$id", trackId));
            if (CacheArt != null && row["cover_id"] is string { Length: > 0 } coverId) foreach (var size in artSizes) try { await CacheArt(coverId, size); } catch { }
        }
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested) { outcome = "cancelled"; }
        catch (Exception downloadError) when (downloadError is HttpRequestException or OperationCanceledException or IOException)
        {
            // Network trouble: back in the queue, keep the partial file, try again in a minute
            outcome = "queued"; error = downloadError is OperationCanceledException ? L.T("The server stopped responding") : downloadError.Message;
            pausedUntil = DateTime.Now.AddMinutes(1); LastProblem = error;
        }
        catch (Exception otherError) { outcome = "failed"; error = otherError.Message; }
        finally
        {
            lock (stateLock) active.Remove(trackId);
            if (outcome == "done") LastProblem = "";
            if (outcome != "cancelled") Run("UPDATE downloads SET state=$state, error=$error, received=CASE WHEN $state='done' THEN size ELSE received END WHERE track_id=$id", ("$state", outcome), ("$error", error), ("$id", trackId));
            NotifyChanged(); Pump();
            if (outcome == "queued") _ = Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(_ => Pump());
        }
    }

    // Cancel: anything not finished (and its partial file). Remove: finished files too, then empty folders
    public void Cancel(IEnumerable<long> trackIds, bool removeFinished)
    {
        var ids = trackIds.ToList();
        var rows = Rows("SELECT track_id, path, state FROM downloads WHERE track_id IN (SELECT value FROM json_each($ids))", ("$ids", JsonSerializer.Serialize(ids)));
        foreach (var row in rows)
        {
            var trackId = Convert.ToInt64(row["track_id"]); var path = (string)row["path"]!;
            if ((string?)row["state"] == "done" && !removeFinished) continue;
            lock (stateLock) if (active.Remove(trackId, out var running)) running.cancel.Cancel();
            Run("DELETE FROM downloads WHERE track_id=$id", ("$id", trackId));
            _ = Task.Run(async () => { await Task.Delay(500); foreach (var file in new[] { path, path + ".part" }) try { File.Delete(file); } catch { } RemoveEmptyFolders(Path.GetDirectoryName(path)); });
        }
        NotifyChanged(); Pump();
    }
    public void CancelAll() => Cancel(Rows("SELECT track_id FROM downloads WHERE state!='done'").Select(row => Convert.ToInt64(row["track_id"])), false);
    public void Retry() { Run("UPDATE downloads SET state='queued', error='' WHERE state='failed'"); pausedUntil = DateTime.MinValue; NotifyChanged(); Pump(); }

    void RemoveEmptyFolders(string? folder)
    {
        while (folder != null && folder.StartsWith(Folder) && folder.Length > Folder.Length && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) { try { Directory.Delete(folder); } catch { return; } folder = Path.GetDirectoryName(folder); }
    }

    // Lyrion track ids can change after a full rescan: re-link downloads to their tracks by file URL
    public void Relink(Func<List<string>, Dictionary<string, long>> trackIdsByUrl)
    {
        var rows = Rows("SELECT track_id, url FROM downloads");
        if (rows.Count == 0) return;
        var current = trackIdsByUrl(rows.Select(row => (string)row["url"]!).ToList());
        foreach (var row in rows)
            if (current.TryGetValue((string)row["url"]!, out var newId) && newId != Convert.ToInt64(row["track_id"])) Run("UPDATE OR IGNORE downloads SET track_id=$new WHERE track_id=$old", ("$new", newId), ("$old", row["track_id"]));
    }

    public List<long> IdsForAlbums(List<string> albumKeys) => Rows("SELECT track_id FROM downloads WHERE album_key IN (SELECT value FROM json_each($keys))", ("$keys", JsonSerializer.Serialize(albumKeys))).Select(row => Convert.ToInt64(row["track_id"])).ToList();
    public List<long> DownloadedIds() => Rows("SELECT track_id FROM downloads WHERE state='done'").Select(row => Convert.ToInt64(row["track_id"])).ToList();
    public bool HasDownloads => Rows("SELECT 1 FROM downloads WHERE state='done' LIMIT 1").Count > 0;

    public OfflineTrack? Find(long trackId)
    {
        var row = Rows("SELECT * FROM downloads WHERE track_id=$id AND state='done'", ("$id", trackId)).FirstOrDefault();
        if (row == null) return null;
        string Text(string name) => Convert.ToString(row[name]) ?? "";
        int Number(string name) => Convert.ToInt32(row[name] ?? 0);
        return new(trackId, Text("path"), Text("title"), Text("artist"), Text("album_title"), Text("cover_id"), Convert.ToDouble(row["duration"] ?? 0), Text("content_type"), Number("sample_rate"), Number("sample_size"), Number("year"), Number("track_no"), Number("disc_no"));
    }

    // What the download manager shows: one group per album, unfinished first, each with its tracks
    public object Snapshot()
    {
        var rows = Rows("SELECT track_id, album_key, album_title, album_artist, cover_id, title, disc_no, track_no, size, received, state, error FROM downloads ORDER BY position");
        Dictionary<long, (CancellationTokenSource cancel, long received, long size)> running; lock (stateLock) running = new(active);
        var groups = rows.GroupBy(row => (string)row["album_key"]!).Select(group =>
        {
            var tracks = group.OrderBy(row => Convert.ToInt32(row["disc_no"] ?? 0)).ThenBy(row => Convert.ToInt32(row["track_no"] ?? 0)).Select(row =>
            {
                var trackId = Convert.ToInt64(row["track_id"]); var live = running.TryGetValue(trackId, out var progress);
                return new { id = trackId, title = row["title"], state = (string)row["state"]!, error = row["error"], received = live ? progress.received : Convert.ToInt64(row["received"]), size = live ? progress.size : Convert.ToInt64(row["size"]) };
            }).ToList();
            var first = group.First();
            return new { key = group.Key, title = first["album_title"], artist = first["album_artist"], cover = first["cover_id"], total = tracks.Count, done = tracks.Count(track => track.state == "done"), failed = tracks.Count(track => track.state == "failed"), downloading = tracks.Count(track => track.state == "downloading"), received = tracks.Sum(track => track.received), size = tracks.Sum(track => track.size), tracks };
        }).ToList();
        var unfinished = groups.Where(group => group.done < group.total).ToList();
        return new { type = "downloads", active = unfinished, finishedTracks = rows.Count(row => (string)row["state"]! == "done"), totalBytes = rows.Where(row => (string)row["state"]! == "done").Sum(row => Convert.ToInt64(row["size"])), waiting = DateTime.Now < pausedUntil, problem = LastProblem, folder = Folder };
    }

    public void Dispose() { lock (stateLock) { foreach (var download in active.Values) download.cancel.Cancel(); active.Clear(); connection?.Dispose(); connection = null; } changeTimer?.Dispose(); }
}
