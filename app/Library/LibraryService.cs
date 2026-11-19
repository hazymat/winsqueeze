using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using WinSqueeze.Settings;
namespace WinSqueeze.Library;

// Keeps the index in step with the server and serves cover art from a disk cache
public sealed class LibraryService
{
    readonly LibrarySync librarySync = new();
    readonly DispatcherTimer refreshTimer = new();
    static readonly HttpClient artClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    public const string ArtHost = "art.winsqueeze";
    public string ProgressText { get; private set; } = "";
    public int ProgressDone { get; private set; }
    public int ProgressTotal { get; private set; }
    public event Action? Changed;     // progress or status
    public event Action? IndexUpdated; // a new index was swapped in
    App App => App.Instance;

    public LibraryQueries Queries => new(App.Settings.ActiveProfile!, App.Settings.Library);

    public void Start()
    {
        librarySync.Progress += (message, done, total) => App.Dispatcher.BeginInvoke(() => { ProgressText = message == "Done" ? "" : message; ProgressDone = done; ProgressTotal = total; Changed?.Invoke(); });
        refreshTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, App.Settings.Library.RefreshMinutes));
        refreshTimer.Tick += async (sender, args) => await RefreshAsync(false);
        refreshTimer.Start();
        retryTimer.Tick += async (sender, args) => { retryTimer.Stop(); await RefreshAsync(false); };
        recoveredTimer.Tick += (sender, args) => { recoveredTimer.Stop(); RecoveredAt = null; Changed?.Invoke(); };
        try { if (App.Settings.ActiveProfile is { } profile && long.TryParse(File.ReadAllText(LastSyncPath(profile)), out var ticks)) LastGoodSync = new DateTime(ticks); } catch { }
        _ = RefreshAsync(false);
    }

    // Sync problems (SPEC.md › Library sync problems): each failure is logged to logs\library.log and retried after 1, 2, 4, 8, then every 10 minutes.
    // A failure that has lasted 3 minutes shows the amber bar in the main window; Settings › Library & cache always shows the details
    public const int BarAfterMinutes = 3;
    readonly DispatcherTimer retryTimer = new(), recoveredTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    public DateTime? FailingSince { get; private set; }
    public DateTime? LastGoodSync { get; private set; }
    public DateTime? NextTry { get; private set; }
    public DateTime? RecoveredAt { get; private set; } // the bar turns green for a few seconds when a shown failure clears
    public int FailedTries { get; private set; }
    public string ErrorText { get; private set; } = ""; // the exception's own words
    public string ProblemKind { get; private set; } = ""; // unreachable, timeout, password, other
    public bool Syncing { get; private set; }
    string hiddenProblem = ""; // the failure the bar was closed on; a different failure, or the same one after a recovery, shows it again
    string ProblemKey => $"{ProblemKind}|{FailingSince?.Ticks}";
    public bool BarHidden => FailingSince != null && hiddenProblem == ProblemKey;
    public bool BarDue => FailingSince is { } since && FailedTries > 1 && DateTime.Now - since >= TimeSpan.FromMinutes(BarAfterMinutes) && !App.IsOffline; // offline, the Downloaded banner already says so
    public void HideBar() { hiddenProblem = ProblemKey; Changed?.Invoke(); }
    public string ProblemText => ProblemKind switch { "unreachable" => L.T("can't reach Lyrion"), "timeout" => L.T("Lyrion isn't answering"), "password" => L.T("Lyrion turned down the user name or password"), _ => L.T("Lyrion sent something unexpected") };
    static string LastSyncPath(ServerProfile profile) => Path.Combine(LibraryDatabase.ProfileFolder(profile), "lastsync.txt");

    public async Task RefreshAsync(bool forceRebuild)
    {
        var profile = App.Settings.ActiveProfile;
        if (profile is not { Host.Length: > 0 } || App.IsOffline || librarySync.IsRunning) return;
        Syncing = true; Changed?.Invoke();
        try
        {
            var updated = await librarySync.SyncIfChangedAsync(profile, App.Settings.Library, forceRebuild);
            SyncWorked(profile);
            if (updated) IndexUpdated?.Invoke();
        }
        catch (Exception syncError) { SyncFailed(syncError, profile); }
        finally { Syncing = false; Changed?.Invoke(); }
    }

    void SyncWorked(ServerProfile profile)
    {
        ProgressText = ""; ProgressDone = ProgressTotal = 0; // a failure's text used to stay until the next full rebuild
        LastGoodSync = DateTime.Now;
        try { File.WriteAllText(LastSyncPath(profile), LastGoodSync.Value.Ticks.ToString()); } catch { }
        if (FailingSince == null) return;
        if (BarDue && !BarHidden) { RecoveredAt = DateTime.Now; recoveredTimer.Stop(); recoveredTimer.Start(); }
        Log($"Sync works again after {FailedTries} failed {(FailedTries == 1 ? "try" : "tries")}");
        FailingSince = NextTry = null; FailedTries = 0; ErrorText = ProblemKind = ""; retryTimer.Stop();
    }

    void SyncFailed(Exception syncError, ServerProfile profile)
    {
        var message = syncError.Message.Replace(profile.Host, "server"); // the repo and logs never carry addresses
        ProgressText = ""; ProgressDone = ProgressTotal = 0;
        var kind = syncError switch { TaskCanceledException or TimeoutException => "timeout", HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } => "password", HttpRequestException { StatusCode: null } => "unreachable", _ => "other" };
        var sameError = FailingSince != null && kind == ProblemKind && message == ErrorText;
        FailingSince ??= DateTime.Now; FailedTries++; ProblemKind = kind; ErrorText = message;
        var waitMinutes = Math.Min(10, 1 << Math.Min(FailedTries - 1, 4));
        NextTry = DateTime.Now.AddMinutes(waitMinutes); retryTimer.Stop(); retryTimer.Interval = TimeSpan.FromMinutes(waitMinutes); retryTimer.Start();
        Log(sameError ? $"Sync failed again, same error (try {FailedTries}, next in {waitMinutes} min)" : $"Sync failed: {syncError.GetType().Name}: {message} (try {FailedTries}, next in {waitMinutes} min)");
    }

    // logs\library.log: sync failures and recoveries, no addresses. Kept to its last 300 lines
    static readonly object logLock = new();
    public static void Log(string line)
    {
        try
        {
            lock (logLock)
            {
                var logPath = Path.Combine(AppPaths.LogsFolder, "library.log");
                var keptLines = File.Exists(logPath) ? File.ReadAllLines(logPath).TakeLast(299).ToList() : new List<string>();
                keptLines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}");
                File.WriteAllLines(logPath, keptLines);
            }
        }
        catch { }
    }
    public static string LogTail(int lineCount) { try { var logPath = Path.Combine(AppPaths.LogsFolder, "library.log"); return File.Exists(logPath) ? string.Join("\n", File.ReadAllLines(logPath).TakeLast(lineCount)) : ""; } catch { return ""; } }

    // Library health (SPEC.md › Library health): built from the local index and kept until the index changes. Rescan asks the server for changes first and checks covers known to be missing again
    Task<JsonElement>? healthRun;
    public Task<JsonElement> HealthAsync(bool rescan) => healthRun is { IsCompleted: false } && !rescan ? healthRun : healthRun = BuildHealthAsync(rescan); // a page opening mid-check waits for the same report
    async Task<JsonElement> BuildHealthAsync(bool rescan)
    {
        var profile = App.Settings.ActiveProfile;
        var databasePath = profile == null ? "" : LibraryDatabase.DatabasePath(profile);
        if (profile == null || !File.Exists(databasePath)) return JsonDocument.Parse("{\"empty\":true}").RootElement.Clone();
        if (rescan) await RefreshAsync(false);
        var cachePath = Path.Combine(LibraryDatabase.ProfileFolder(profile), "health.json");
        var cacheKey = await Task.Run(() => { using var connection = LibraryDatabase.Open(databasePath, true); return $"{LibraryDatabase.Meta(connection, "signature")}|{LibraryHealth.Version}"; });
        if (!rescan) try { using var cached = JsonDocument.Parse(await File.ReadAllTextAsync(cachePath)); if (cached.RootElement.GetProperty("cacheKey").GetString() == cacheKey) return cached.RootElement.Clone(); } catch { }
        if (rescan && !App.IsOffline) foreach (var emptyFile in new DirectoryInfo(LibraryDatabase.ArtFolder(profile)).EnumerateFiles().Where(file => file.Length == 0).ToList()) try { emptyFile.Delete(); } catch { }
        var coverIds = await Task.Run(() => LibraryHealth.CoverIds(databasePath));
        var coversWithoutArt = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        if (!App.IsOffline) await PlaceholderAsync(profile, 120); // fetched once before the parallel checks share it
        using (var gate = new SemaphoreSlim(8))
            await Task.WhenAll(coverIds.Select(async coverId => { await gate.WaitAsync(); try { if (!await HasArtAsync(profile, coverId)) coversWithoutArt[coverId] = true; } finally { gate.Release(); } }));
        var reportJson = await Task.Run(() => JsonSerializer.Serialize(LibraryHealth.Build(databasePath, cacheKey, coversWithoutArt.Keys)));
        try { await File.WriteAllTextAsync(cachePath, reportJson); } catch { }
        return JsonDocument.Parse(reportJson).RootElement.Clone();
    }

    // Whether the server has real art for a cover; offline, a cover not seen yet counts as fine
    async Task<bool> HasArtAsync(ServerProfile profile, string coverId)
    {
        coverId = new string(coverId.Where(char.IsLetterOrDigit).ToArray());
        var cachePath = Path.Combine(LibraryDatabase.ArtFolder(profile), $"{coverId}_120");
        if (App.IsOffline) return !File.Exists(cachePath) || new FileInfo(cachePath).Length > 0;
        try { return await GetArtAsync(coverId, 120) != null; } catch { return true; }
    }

    // https://art.winsqueeze/<coverId>/<size> -> cached copy of the server's resized cover
    public void AttachArtHandler(CoreWebView2 webView)
    {
        webView.AddWebResourceRequestedFilter($"https://{ArtHost}/*", CoreWebView2WebResourceContext.Image);
        webView.WebResourceRequested += async (sender, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                var pathParts = new Uri(args.Request.Uri).AbsolutePath.Trim('/').Split('/');
                // https://art.winsqueeze/server/<escaped server path> -> an image the server serves itself (favourite and radio icons, imageproxy)
                if (pathParts[0] == "server" && pathParts.Length > 1 && App.Settings.ActiveProfile is { Host.Length: > 0 } artProfile)
                {
                    var serverPath = Uri.UnescapeDataString(string.Join("/", pathParts.Skip(1))).TrimStart('/');
                    using var serverResponse = await artClient.GetAsync($"http://{artProfile.Host}:{artProfile.Port}/{serverPath}");
                    var serverBytes = serverResponse.IsSuccessStatusCode ? await serverResponse.Content.ReadAsByteArrayAsync() : null;
                    args.Response = serverBytes == null ? webView.Environment.CreateWebResourceResponse(null, 404, "Not found", "") : webView.Environment.CreateWebResourceResponse(new MemoryStream(serverBytes), 200, "OK", $"Content-Type: {SniffType(serverBytes)}\nCache-Control: max-age=86400");
                    return;
                }
                var coverId = new string(pathParts[0].Where(char.IsLetterOrDigit).ToArray());
                var size = pathParts.Length > 1 && int.TryParse(pathParts[1], out var requestedSize) ? Math.Clamp(requestedSize, 32, 1200) : 300;
                var artBytes = await GetArtAsync(coverId, size);
                args.Response = artBytes == null ? webView.Environment.CreateWebResourceResponse(null, 404, "Not found", "") : webView.Environment.CreateWebResourceResponse(new MemoryStream(artBytes), 200, "OK", $"Content-Type: {SniffType(artBytes)}\nCache-Control: max-age=31536000\nAccess-Control-Allow-Origin: *");
            }
            catch { args.Response = webView.Environment.CreateWebResourceResponse(null, 500, "Error", ""); }
            finally { deferral.Complete(); }
        };
    }

    readonly Dictionary<int, byte[]> placeholders = new();
    async Task<byte[]> PlaceholderAsync(ServerProfile profile, int size)
    {
        if (placeholders.TryGetValue(size, out var known)) return known;
        try { using var response = await artClient.GetAsync($"http://{profile.Host}:{profile.Port}/music/0/cover_{size}x{size}_o"); known = response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : Array.Empty<byte>(); } catch { known = Array.Empty<byte>(); }
        if (known.Length > 0) placeholders[size] = known;
        return known.Length > 0 ? known : new byte[] { 0 };
    }

    async Task<byte[]?> GetArtAsync(string coverId, int size)
    {
        var profile = App.Settings.ActiveProfile;
        if (profile is not { Host.Length: > 0 } || coverId == "") return null;
        var cachePath = Path.Combine(LibraryDatabase.ArtFolder(profile), $"{coverId}_{size}");
        if (App.IsOffline) { var offlineBytes = File.Exists(cachePath) ? await File.ReadAllBytesAsync(cachePath) : Array.Empty<byte>(); return offlineBytes.Length == 0 ? null : offlineBytes; } // offline: only what's cached
        var placeholder = await PlaceholderAsync(profile, size);
        if (File.Exists(cachePath)) { var cachedBytes = await File.ReadAllBytesAsync(cachePath); return cachedBytes.Length == 0 || cachedBytes.AsSpan().SequenceEqual(placeholder) ? null : cachedBytes; } // older caches may hold the placeholder
        using var response = await artClient.GetAsync($"http://{profile.Host}:{profile.Port}/music/{coverId}/cover_{size}x{size}_o");
        if (!response.IsSuccessStatusCode) return null;
        var artBytes = await response.Content.ReadAsByteArrayAsync();
        // No real art: Lyrion sends its own framed placeholder; report "none" (cached as an empty file) so the UI shows its own rounded one
        if (artBytes.AsSpan().SequenceEqual(placeholder)) artBytes = Array.Empty<byte>();
        await File.WriteAllBytesAsync(cachePath, artBytes);
        return artBytes.Length == 0 ? null : artBytes;
    }

    // Downloads keep their covers so they show offline
    public async Task CacheArtAsync(string coverId, int size) => await GetArtAsync(new string(coverId.Where(char.IsLetterOrDigit).ToArray()), size);

    public void ClearArtCache() { var profile = App.Settings.ActiveProfile; if (profile != null) { Directory.Delete(LibraryDatabase.ArtFolder(profile), true); } }

    public long CacheBytes()
    {
        var profile = App.Settings.ActiveProfile;
        return profile == null ? 0 : new DirectoryInfo(LibraryDatabase.ProfileFolder(profile)).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
    }

    static string SniffType(byte[] imageBytes) => imageBytes.Length > 3 && imageBytes[0] == 0x89 && imageBytes[1] == 0x50 ? "image/png" : imageBytes.Length > 3 && imageBytes[0] == 0x47 ? "image/gif" : "image/jpeg";
}
