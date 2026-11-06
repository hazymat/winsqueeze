using System.IO;
using System.Net.Http;
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
        _ = RefreshAsync(false);
    }

    public async Task RefreshAsync(bool forceRebuild)
    {
        var profile = App.Settings.ActiveProfile;
        if (profile is not { Host.Length: > 0 }) return;
        try
        {
            if (await librarySync.SyncIfChangedAsync(profile, App.Settings.Library, forceRebuild)) IndexUpdated?.Invoke();
        }
        catch (Exception syncError) { ProgressText = $"Library sync failed: {syncError.Message}"; Changed?.Invoke(); }
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
        if (File.Exists(cachePath)) { var cachedBytes = await File.ReadAllBytesAsync(cachePath); return cachedBytes.Length == 0 || cachedBytes.AsSpan().SequenceEqual(await PlaceholderAsync(profile, size)) ? null : cachedBytes; } // older caches may hold the placeholder
        using var response = await artClient.GetAsync($"http://{profile.Host}:{profile.Port}/music/{coverId}/cover_{size}x{size}_o");
        if (!response.IsSuccessStatusCode) return null;
        var artBytes = await response.Content.ReadAsByteArrayAsync();
        // No real art: Lyrion sends its own framed placeholder; report "none" (cached as an empty file) so the UI shows its own rounded one
        if (artBytes.AsSpan().SequenceEqual(await PlaceholderAsync(profile, size))) artBytes = Array.Empty<byte>();
        await File.WriteAllBytesAsync(cachePath, artBytes);
        return artBytes.Length == 0 ? null : artBytes;
    }

    public void ClearArtCache() { var profile = App.Settings.ActiveProfile; if (profile != null) { Directory.Delete(LibraryDatabase.ArtFolder(profile), true); } }

    public long CacheBytes()
    {
        var profile = App.Settings.ActiveProfile;
        return profile == null ? 0 : new DirectoryInfo(LibraryDatabase.ProfileFolder(profile)).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
    }

    static string SniffType(byte[] imageBytes) => imageBytes.Length > 3 && imageBytes[0] == 0x89 && imageBytes[1] == 0x50 ? "image/png" : imageBytes.Length > 3 && imageBytes[0] == 0x47 ? "image/gif" : "image/jpeg";
}
