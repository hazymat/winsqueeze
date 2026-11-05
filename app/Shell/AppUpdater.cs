using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;
using WinSqueeze.Settings;
namespace WinSqueeze.Shell;

public record ReleaseEntry(string Tag, string Version, string Title, bool IsDev, DateTime Published, string Notes, string Url, string DiscussionUrl);

// Background update check, silent download/stage, one-click restart, and switching to any published version (SPEC.md › Updates)
public sealed class AppUpdater
{
    public const string RepoUrl = "https://github.com/hazymat/winsqueeze";
    static readonly HttpClient githubClient = CreateGithubClient();
    readonly DispatcherTimer checkTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    UpdateManager? readyManager;
    UpdateInfo? readyUpdate;
    bool checking;

    public string StatusText { get; private set; } = "";
    string portableStagedPath = "", portableReadyVersion = "";
    public string ReadyVersion => readyUpdate?.TargetFullRelease.Version.ToString() ?? portableReadyVersion;
    public event Action? Changed;
    App App => App.Instance;

    static string AssemblyVersion => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public string CurrentVersion => AppPaths.IsPortable ? AssemblyVersion : SafeManager(App.Settings.General.UpdateChannel)?.CurrentVersion?.ToString() ?? AssemblyVersion;
    // Portable copies update themselves; installed copies through Velopack
    public bool IsInstalled => AppPaths.IsPortable || SafeManager(App.Settings.General.UpdateChannel)?.IsInstalled == true;

    public void Start()
    {
        checkTimer.Tick += async (sender, args) => { if (App.Settings.General.AutoUpdate) await CheckNowAsync(); };
        checkTimer.Start();
        if (App.Settings.General.AutoUpdate) _ = CheckNowAsync();
    }

    // Channel feed for normal updates; a single release's assets when switching to a specific version
    static UpdateManager CreateManager(string channel, string releaseTag = "")
    {
        IUpdateSource updateSource = releaseTag == "" ? new GithubSource(RepoUrl, null, channel == "dev") : new SimpleWebSource($"{RepoUrl}/releases/download/{releaseTag}/");
        return new UpdateManager(updateSource, new UpdateOptions { ExplicitChannel = channel, AllowVersionDowngrade = releaseTag != "" });
    }

    static UpdateManager? SafeManager(string channel) { try { return CreateManager(channel); } catch { return null; } }

    public async Task CheckNowAsync()
    {
        if (checking) return;
        checking = true;
        try
        {
            // GitHub's release feed sometimes returns a 502/504 for a moment: retry quietly before reporting a failure
            for (var attempt = 1; ; attempt++)
            {
                try { await CheckOnceAsync(); return; }
                catch (HttpRequestException networkError) when (attempt < 3) { SetStatus($"GitHub didn't answer ({networkError.StatusCode?.ToString() ?? "network"}), retrying…"); await Task.Delay(attempt * 3000); }
            }
        }
        catch (HttpRequestException) { SetStatus($"GitHub isn't answering right now. WinSqueeze will try again automatically (last try {DateTime.Now:HH:mm})."); }
        catch (Exception updateError) { SetStatus($"Update check failed: {updateError.Message}"); }
        finally { checking = false; }
    }

    async Task CheckOnceAsync()
    {
        {
            if (AppPaths.IsPortable) { await CheckPortableAsync(); return; }
            var updateManager = CreateManager(App.Settings.General.UpdateChannel);
            if (!updateManager.IsInstalled) { SetStatus("Updates work once WinSqueeze is installed with Setup.exe (this copy is running from a zip)."); return; }
            SetStatus("Checking for updates…");
            var foundUpdate = await updateManager.CheckForUpdatesAsync();
            if (foundUpdate == null) { SetStatus($"Up to date ({updateManager.CurrentVersion}). Checked {DateTime.Now:HH:mm}."); return; }
            SetStatus($"Downloading {foundUpdate.TargetFullRelease.Version}…");
            await updateManager.DownloadUpdatesAsync(foundUpdate);
            readyManager = updateManager; readyUpdate = foundUpdate;
            SetStatus($"WinSqueeze {ReadyVersion} is downloaded and ready. Restart to update.");
            App.ShowTip("Update ready", $"WinSqueeze {ReadyVersion} is ready. Click Restart to update in the window or Settings › About.");
        }
    }

    // Relaunches into the staged version, telling it where playback was so it can carry on
    public async Task RestartToUpdateAsync()
    {
        if (portableStagedPath != "") { await SwapPortableAndRestartAsync(portableStagedPath); return; }
        if (readyManager == null || readyUpdate == null) return;
        var restartArguments = await App.ResumeArgumentsAsync();
        App.PrepareForRestart();
        readyManager.ApplyUpdatesAndRestart(readyUpdate.TargetFullRelease, restartArguments);
    }

    public async Task SwitchToAsync(ReleaseEntry release)
    {
        try
        {
            if (AppPaths.IsPortable) { SetStatus($"Downloading {release.Version}…"); await SwapPortableAndRestartAsync(await DownloadPortableAsync(release.Tag)); return; }
            SetStatus($"Downloading {release.Version}…");
            var switchManager = CreateManager(release.IsDev ? "dev" : "stable", release.Tag);
            var switchUpdate = await switchManager.CheckForUpdatesAsync();
            if (switchUpdate == null) { SetStatus($"{release.Version} is already running."); return; }
            await switchManager.DownloadUpdatesAsync(switchUpdate);
            var restartArguments = await App.ResumeArgumentsAsync();
            App.PrepareForRestart();
            switchManager.ApplyUpdatesAndRestart(switchUpdate.TargetFullRelease, restartArguments);
        }
        catch (Exception switchError) { SetStatus($"Could not switch to {release.Version}: {switchError.Message}"); }
    }

    public async Task<List<ReleaseEntry>> ListReleasesAsync(bool includeDev)
    {
        var releasesJson = await githubClient.GetStringAsync("https://api.github.com/repos/hazymat/winsqueeze/releases?per_page=60");
        using var releasesDocument = JsonDocument.Parse(releasesJson);
        var releases = new List<ReleaseEntry>();
        foreach (var releaseElement in releasesDocument.RootElement.EnumerateArray())
        {
            if (releaseElement.GetProperty("draft").GetBoolean()) continue;
            var isDev = releaseElement.GetProperty("prerelease").GetBoolean();
            if (isDev && !includeDev) continue;
            var tag = releaseElement.GetProperty("tag_name").GetString() ?? "";
            releases.Add(new ReleaseEntry(tag, tag.TrimStart('v'), releaseElement.GetProperty("name").GetString() ?? tag, isDev, releaseElement.GetProperty("published_at").GetDateTime().ToLocalTime(), releaseElement.GetProperty("body").GetString() ?? "", releaseElement.GetProperty("html_url").GetString() ?? "", releaseElement.TryGetProperty("discussion_url", out var discussionUrl) ? discussionUrl.GetString() ?? "" : ""));
        }
        return releases;
    }

    // ---- Portable single-file exe: download the release's exe next to this one, swap on restart ----
    async Task CheckPortableAsync()
    {
        SetStatus("Checking for updates…");
        var latest = (await ListReleasesAsync(App.Settings.General.UpdateChannel == "dev")).FirstOrDefault();
        if (latest == null || !Version.TryParse(latest.Version, out var latestVersion) || latestVersion <= Version.Parse(AssemblyVersion)) { SetStatus($"Up to date ({AssemblyVersion}). Checked {DateTime.Now:HH:mm}."); return; }
        SetStatus($"Downloading {latest.Version}…");
        portableStagedPath = await DownloadPortableAsync(latest.Tag);
        portableReadyVersion = latest.Version;
        SetStatus($"WinSqueeze {ReadyVersion} is downloaded and ready. Restart to update.");
        App.ShowTip("Update ready", $"WinSqueeze {ReadyVersion} is ready. Click Restart to update in the window or Settings › About.");
    }

    static async Task<string> DownloadPortableAsync(string releaseTag)
    {
        var stagedPath = AppPaths.ExePath + ".update";
        using var download = await githubClient.GetAsync($"{RepoUrl}/releases/download/{releaseTag}/WinSqueeze-Portable.exe", HttpCompletionOption.ResponseHeadersRead);
        download.EnsureSuccessStatusCode();
        await using (var stagedFile = File.Create(stagedPath)) await download.Content.CopyToAsync(stagedFile);
        return stagedPath;
    }

    // Windows lets a running exe be renamed, so: current -> .old, staged -> current, start it, exit
    async Task SwapPortableAndRestartAsync(string stagedPath)
    {
        var restartArguments = await App.ResumeArgumentsAsync();
        var oldPath = AppPaths.ExePath + ".old";
        if (File.Exists(oldPath)) File.Delete(oldPath);
        File.Move(AppPaths.ExePath, oldPath);
        File.Move(stagedPath, AppPaths.ExePath);
        App.PrepareForRestart();
        var restartInfo = new ProcessStartInfo(AppPaths.ExePath) { UseShellExecute = false, WorkingDirectory = AppPaths.ExeFolder };
        foreach (var argument in restartArguments) restartInfo.ArgumentList.Add(argument);
        Process.Start(restartInfo);
        App.Shutdown();
    }

    public static void CleanUpAfterPortableUpdate() { try { File.Delete(AppPaths.ExePath + ".old"); File.Delete(AppPaths.ExePath + ".update"); } catch { } }

    void SetStatus(string newStatus) { StatusText = newStatus; Changed?.Invoke(); }

    static HttpClient CreateGithubClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WinSqueeze");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
