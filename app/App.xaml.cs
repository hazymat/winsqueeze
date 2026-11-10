using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WinSqueeze.Audio;
using WinSqueeze.Library;
using WinSqueeze.Server;
using WinSqueeze.Settings;
using WinSqueeze.Shell;
namespace WinSqueeze;

public partial class App : Application
{
    public static App Instance => (App)Current;
    public AppSettings Settings { get; private set; } = null!;
    public SqueezeliteHost Player { get; } = new();
    public AppUpdater Updater { get; } = new();
    public PlayerSession Session { get; } = new();
    public LibraryService Library { get; } = new();
    public MediaControls? Media { get; private set; }
    public VisualiserReader Visualiser { get; } = new();
    public Equaliser Equaliser { get; } = new();
    public Recorder Recorder { get; } = new();
    public Offline.DownloadManager Downloads { get; } = new();
    public PlayHistory History { get; } = new();
    public DamagedFiles Damaged { get; } = new();
    public TrackInfo TrackInfo { get; } = new();
    public event Action<DamagedFiles.DamagedFile, string>? DamagedFound;
    public event Action? HistoryChanged;
    public Offline.OfflineServer? Offline { get; private set; } // set while playing downloads without the server
    public bool IsOffline => Offline != null;
    public bool ServerBack { get; private set; } // offline, and the server answers again
    EqualiserWindow? equaliserWindow;
    LyricsWindow? lyricsWindow;
    public List<OutputDevice> Devices { get; private set; } = new();
    public bool UsingFallbackDevice { get; private set; }
    readonly WindowsDefaultDevice windowsDefault = new();
    public string WindowsDefaultName => windowsDefault.Name;
    public bool IsQuitting { get; private set; }
    public event Action? DevicesChanged;
    public event Action? StatusChanged;
    public event Action? LibraryChanged;
    public event Action? DownloadsChanged;

    MainWindow mainWindow = null!;
    TrayIcon trayIcon = null!;
    SettingsWindow? settingsWindow;
    readonly SingleInstance singleInstance = new();
    readonly DispatcherTimer deviceChangeTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    const int WM_DEVICECHANGE = 0x0219, DBT_DEVNODES_CHANGED = 0x0007, WM_HOTKEY = 0x0312;

    // ---- Global hotkeys (Settings › Shortcuts, off by default): Ctrl+Alt with Space, arrows ----
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
    static readonly (int id, uint virtualKey)[] globalHotkeys = { (1, 0x20), (2, 0x25), (3, 0x27), (4, 0x26), (5, 0x28) }; // space, left, right, up, down
    public List<string> ApplyGlobalHotkeys()
    {
        var windowHandle = new WindowInteropHelper(mainWindow).EnsureHandle();
        var taken = new List<string>();
        foreach (var (id, virtualKey) in globalHotkeys)
        {
            UnregisterHotKey(windowHandle, id);
            if (Settings.General.GlobalHotkeys && !RegisterHotKey(windowHandle, id, 0x0001 | 0x0002 | 0x4000 /*Alt, Ctrl, no repeat*/, virtualKey)) taken.Add(new[] { "", "Space", "Left", "Right", "Up", "Down" }[id]);
        }
        return taken; // combinations another app already owns
    }
    async Task OnGlobalHotkeyAsync(int id)
    {
        switch (id)
        {
            case 1: await SendPlayerCommandAsync("pause"); break;
            case 2: await SendPlayerCommandAsync("playlist", "index", "-1"); break;
            case 3: await SendPlayerCommandAsync("playlist", "index", "+1"); break;
            case 4: await SendPlayerCommandAsync("mixer", "volume", "+5"); break;
            case 5: await SendPlayerCommandAsync("mixer", "volume", "-5"); break;
        }
    }

    // ---- Track-change notifications (Settings › Notifications, off by default) ----
    string lastTrackKey = "";
    void NotifyTrackChange(System.Text.Json.JsonElement status)
    {
        if (status.ValueKind != JsonValueKind.Object || MediaControls.CurrentTrack(status) is not { } track) return;
        var title = LyrionClient.ReadString(track, "title"); var artist = LyrionClient.ReadString(track, "artist");
        var trackKey = title + "\u0001" + artist;
        if (trackKey == lastTrackKey) return;
        var firstTrack = lastTrackKey == ""; lastTrackKey = trackKey;
        if (!firstTrack && Settings.General.TrackToasts && LyrionClient.ReadString(status, "mode") == "play") trayIcon.ShowTip(title, $"{artist}{(LyrionClient.ReadString(track, "album") is { Length: > 0 } album ? " · " + album : "")}");
    }

    // This PC's ReplayGain and crossfade (Lyrion player prefs), read for the fidelity badge; Settings › Playback changes them
    public string OwnReplayGainMode = "0", OwnTransitionType = "0";
    public async Task LoadOwnPlayerPrefsAsync()
    {
        try
        {
            OwnReplayGainMode = LyrionClient.ReadString(await ServerRequestAsync(Settings.PlayerMac, "playerpref", "replayGainMode", "?"), "_p2");
            OwnTransitionType = LyrionClient.ReadString(await ServerRequestAsync(Settings.PlayerMac, "playerpref", "transitionType", "?"), "_p2");
            if (OwnReplayGainMode == "") OwnReplayGainMode = "0"; if (OwnTransitionType == "") OwnTransitionType = "0";
            StatusChanged?.Invoke();
        }
        catch { }
    }

    // Read-only fidelity check (SPEC.md › Playback and fidelity): warn if Lyrion caps this player's bitrate, which forces transcoding. Never changes it
    async Task CheckServerTranscodingAsync()
    {
        await Task.Delay(8000); // give the player time to register with the server
        await LoadOwnPlayerPrefsAsync();
        try
        {
            if (Settings.ActiveProfile is not { Host.Length: > 0 } profile) return;
            using var lyrionClient = new LyrionClient(profile);
            var limit = LyrionClient.ReadString(await lyrionClient.RequestAsync(Settings.PlayerMac, "playerpref", "maxBitrate", "?"), "_p2");
            if (limit is not ("" or "0")) trayIcon.ShowTip(L.T("The server is transcoding"), L.T("Lyrion limits {player} to {limit} kbps, so music isn't lossless. In Lyrion: Settings › Player › Audio › Bitrate limiting › No limit.", ("player", Settings.Audio.PlayerName), ("limit", limit)));
        }
        catch { }
    }

    // ---- Tray actions ----
    public void SetWindowMode(string mode) { ShowMainWindow(); mainWindow.SetWindowMode(mode); }
    public void NewPlaylist() { ShowMainWindow(); mainWindow.PushNewPlaylist(); }
    public void OpenSection(string section) { ShowMainWindow(); mainWindow.PushOpenSection(section); }
    public void SetSkin(string skin) { Settings.General.Skin = skin; Settings.Save(); ApplySkin(); }
    public async Task SelectPlayerAsync(string playerId) { Session.SelectedPlayerId = playerId == Settings.PlayerMac ? "" : playerId; await Session.RequestAsync(true, "status", "-", 1); }

    protected override async void OnStartup(StartupEventArgs startupArgs)
    {
        if (!singleInstance.Claim()) { Shutdown(); return; } // second launch just brings the running app forward
        singleInstance.OnShowRequest(() => Dispatcher.BeginInvoke(() => { ShowMainWindow(); singleInstance.Answer(); }));
        if (AppPaths.IsPortable) AppUpdater.CleanUpAfterPortableUpdate();
        CrashReporter.Install(this);
        Settings = AppSettings.Load();
        L.Load(Settings.General.Language);
        mainWindow = new MainWindow();
        trayIcon = new TrayIcon();
        Player.StatusChanged += () => Dispatcher.BeginInvoke(OnPlayerStatusChanged);
        Player.ExclusiveFailed += (deviceName, reason) => Dispatcher.BeginInvoke(async () => { trayIcon.ShowTip(L.T("Playing in shared mode"), L.T("{device} refused exclusive mode: {reason}. WinSqueeze is playing it shared.", ("device", deviceName), ("reason", reason))); await StartPlayerAsync(true); });
        Damaged.Load(); Damaged.PlayerRequest = command => ServerRequestAsync(Settings.PlayerMac, command);
        Player.DecodeLine += decodeLine => Dispatcher.BeginInvoke(() => Damaged.OnLogLine(decodeLine));
        Damaged.Found += OnDamagedFound;
        HwndSource.FromHwnd(new WindowInteropHelper(mainWindow).EnsureHandle()).AddHook(WindowMessageHook);
        deviceChangeTimer.Tick += async (sender, args) => { deviceChangeTimer.Stop(); await OnDevicesChangedAsync(); };
        windowsDefault.Changed += () => Dispatcher.BeginInvoke(() => { deviceChangeTimer.Stop(); deviceChangeTimer.Start(); });
        // A restart for an update brings the window back exactly as it was (--window=normal|maximised|minimised|hidden,left,top,width,height); otherwise the start-minimised setting applies
        mainWindow.RestoreBoundsForDisplays();
        var windowArgument = startupArgs.Args.FirstOrDefault(argument => argument.StartsWith("--window="));
        if (windowArgument != null) mainWindow.RestoreFromArgument(windowArgument["--window=".Length..]);
        else if (!(Settings.General.StartMinimised || startupArgs.Args.Contains("--minimised"))) mainWindow.Show();
        else if (!Settings.General.ShowTrayIcon) { mainWindow.WindowState = WindowState.Minimized; mainWindow.Show(); } // no tray icon: start minimised on the taskbar instead
        if (Settings.ActiveProfile is not { Host.Length: > 0 }) OpenSettings("Servers");
        Updater.Changed += () => Dispatcher.BeginInvoke(() => StatusChanged?.Invoke());
        Updater.Start();
        RefreshIconsAfterUpdate();
        if (CrashReporter.TakeUnseenReport()) ShowTip(L.T("WinSqueeze had a problem last time"), L.T("It crashed or stopped responding. Details are in Settings › About › Open logs (crash.log)."));
        try { Media = new MediaControls(); } catch { } // SMTC needs Windows 10 1903+
        Session.StatusUpdated += () => { if (Settings.ActiveProfile is { } profile) Media?.Update(Session.LastStatus, $"http://{profile.Host}:{profile.Port}"); trayIcon.RefreshNowPlaying(Session.LastStatus); NotifyTrackChange(Session.LastStatus); History.OnStatus(Session.LastStatus); Recorder.OnStatus(Session.LastStatus, Session.SelectedPlayerId == ""); };
        Recorder.Changed += () => Dispatcher.BeginInvoke(() => { UpdatePolling(); StatusChanged?.Invoke(); });
        ApplyGlobalHotkeys();
        Downloads.CacheArt = Library.CacheArtAsync;
        Downloads.Open(Settings.ActiveProfile);
        History.Open(Settings.ActiveProfile); History.Changed += () => Dispatcher.BeginInvoke(() => HistoryChanged?.Invoke());
        Downloads.Changed += () => Dispatcher.BeginInvoke(() => DownloadsChanged?.Invoke());
        Library.IndexUpdated += () => { var queries = Library.Queries; Downloads.Relink(queries.TrackIdsByUrl); DownloadsChanged?.Invoke(); };
        Session.ServerUnreachable += () => _ = GoOfflineAsync(true);
        serverBackTimer.Tick += async (sender, args) => await CheckServerBackAsync();
        Session.Start();
        Library.Start();
        await CheckOtherPlayersAsync();
        await RefreshDevicesAsync();
        // After an update or version switch, carry on from where playback was (no startup check: it was playing a moment ago).
        // With "Resume playing after an update" unticked, the held startup check runs instead and pauses whatever the server restarts
        var resumeArgument = startupArgs.Args.FirstOrDefault(argument => argument.StartsWith("--resume="));
        restartedForUpdate = startupArgs.Args.Any(argument => argument.StartsWith("--window="));
        startupPlaybackChecked = restartedForUpdate && Settings.General.ResumeAfterUpdate;
        await StartPlayerAsync(false);
        _ = CheckServerTranscodingAsync();
        if (resumeArgument != null && Settings.General.ResumeAfterUpdate && Player.State == PlayerState.Running && double.TryParse(resumeArgument["--resume=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var resumeSeconds)) await ResumeAtAsync(resumeSeconds);
    }

    // Windows caches shortcut and taskbar icons; after a version change, ask the shell to refresh them so a new icon shows up
    [System.Runtime.InteropServices.DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    void RefreshIconsAfterUpdate()
    {
        versionBeforeThisRun = Settings.General.LastRunVersion;
        if (Settings.General.LastRunVersion == Updater.CurrentVersion) return;
        Settings.General.LastRunVersion = Updater.CurrentVersion; Settings.Save();
        try { SHChangeNotify(0x08000000 /*SHCNE_ASSOCCHANGED*/, 0, IntPtr.Zero, IntPtr.Zero); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ie4uinit.exe", "-show") { CreateNoWindow = true, UseShellExecute = false })?.Dispose(); } catch { }
    }

    public void ApplyWindowStyle() => mainWindow.ApplyWindowStyle();
    public void ApplySkin() => mainWindow.PushSkin();
    public void ApplyUiScale() => mainWindow.ApplyUiScale();
    public void RefreshInactiveGlass() => mainWindow.RefreshInactiveGlass();
    // After an update: the release notes of every version since the one that last ran (including skipped ones); stable users get the stable summary first, every dev build's changes behind an expander
    bool whatsNewChecked;
    string versionBeforeThisRun = "";
    public async Task ShowWhatsNewAsync()
    {
        if (whatsNewChecked) return;
        whatsNewChecked = true;
        string previousVersion = versionBeforeThisRun, currentVersion = Updater.CurrentVersion;
        if (previousVersion == "" || !AppUpdater.IsNewer(currentVersion, previousVersion)) return;
        try
        {
            var releases = (await Updater.ListReleasesAsync(true)).Where(release => AppUpdater.IsNewer(release.Version, previousVersion) && !AppUpdater.IsNewer(release.Version, currentVersion)).OrderByDescending(release => Version.Parse(release.Version)).ToList();
            if (releases.Count > 0) mainWindow.PushWhatsNew(previousVersion, currentVersion, releases, Settings.General.UpdateChannel != "dev");
        }
        catch (Exception error) { CrashReporter.Write("What's new: release notes unavailable", error, false); }
    }

    public void RaiseStatusChanged() => StatusChanged?.Invoke();
    // The EQ floats beside the main window (docks to its edges); opening again brings it forward
    public void OpenEqualiser()
    {
        if (equaliserWindow == null) { equaliserWindow = new EqualiserWindow(mainWindow); equaliserWindow.Closed += (sender, args) => equaliserWindow = null; }
        equaliserWindow.ShowByUser();
    }
    public void ToggleEqualiser() { if (equaliserWindow is { IsVisible: true }) equaliserWindow.HideByUser(); else OpenEqualiser(); } // the transport's analyser opens and closes it
    // Lyrics on their own (SPEC.md › Lyrics view): full screen from the big player, a window of their own from the mini player
    public void OpenLyrics(bool fullScreen)
    {
        if (lyricsWindow == null)
        {
            lyricsWindow = new LyricsWindow { OpenedFullScreen = fullScreen };
            lyricsWindow.Closed += (sender, args) => lyricsWindow = null;
            lyricsWindow.PlaceOn(mainWindow);
            if (fullScreen) lyricsWindow.SourceInitialized += (sender, args) => lyricsWindow?.SetFullScreen(true);
            lyricsWindow.Show();
        }
        else if (fullScreen) lyricsWindow.SetFullScreen(true);
        lyricsWindow?.Activate();
    }
    // Lyrion is polled every second while either window is on screen
    public void UpdatePolling() => Session.SetVisible(mainWindow.IsVisible || lyricsWindow is { IsVisible: true } || Recorder.Recording); // a recording splits on song changes, so keep polling every second
    public void ApplyMeters() { Visualiser.Tuning = Settings.Meters; mainWindow.PushMeterSettings(); }

    public async Task<string[]> ResumeArgumentsAsync()
    {
        (string mode, double seconds) playbackState = Player.State == PlayerState.Running ? await ReadPlaybackStateAsync() : ("", 0);
        var restartArguments = new List<string> { "--window=" + mainWindow.DescribeForRestart() };
        if (playbackState.mode == "play") restartArguments.Add("--resume=" + playbackState.seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return restartArguments.ToArray();
    }

    // Stops Squeezelite and releases the single-instance lock so the new version can start straight away
    public void PrepareForRestart()
    {
        mainWindow.SaveBounds(); Settings.Save();
        IsQuitting = true;
        Recorder.Dispose(); // finish any recording before Squeezelite stops
        Player.Dispose();
        trayIcon.Dispose();
        singleInstance.Release();
    }

    // Starts a fresh copy of this version (a new language), carrying on playback as an update restart does
    public async Task RestartAsync()
    {
        var restartArguments = await ResumeArgumentsAsync();
        PrepareForRestart();
        var restartInfo = new System.Diagnostics.ProcessStartInfo(AppPaths.ExePath) { UseShellExecute = false, WorkingDirectory = AppPaths.ExeFolder };
        foreach (var argument in restartArguments) restartInfo.ArgumentList.Add(argument);
        System.Diagnostics.Process.Start(restartInfo);
        Shutdown();
    }

    // Another Squeezelite (even renamed) can hold the output device and shows up as a second player
    async Task CheckOtherPlayersAsync()
    {
        var otherAction = Settings.General.OtherSqueezelite;
        if (otherAction == OtherSqueezeliteAction.Leave) return;
        var otherPlayers = await Task.Run(() => OtherPlayerDetector.Find(0));
        var problems = new List<string>();
        foreach (var otherPlayer in otherPlayers)
        {
            var startupText = otherPlayer.StartupEntries.Count == 0 ? "" : "\n\n" + L.T("It starts at login via: {entries}.", ("entries", string.Join(", ", otherPlayer.StartupEntries.Select(entry => entry.Description))));
            var choice = otherAction == OtherSqueezeliteAction.Stop ? MessageBoxResult.Yes : MessageBox.Show(
                L.T("Another Squeezelite is running:") + $"\n{otherPlayer.ExePath} ({L.T("process {id}", ("id", otherPlayer.ProcessId))})\n\n" + L.T("It can hold your output device and shows up as a second player on the server.") + startupText + "\n\n" +
                (otherPlayer.StartupEntries.Count == 0 ? L.T("Stop it now?") : L.T("Yes: stop it and switch off its startup entry (moved aside, not deleted).") + "\n" + L.T("No: stop it this time only.") + "\n" + L.T("Cancel: leave it running.")),
                "WinSqueeze", otherPlayer.StartupEntries.Count == 0 ? MessageBoxButton.YesNo : MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Yes) problems.AddRange(OtherPlayerDetector.Stop(otherPlayer, otherAction == OtherSqueezeliteAction.Ask));
            else if (choice == MessageBoxResult.No && otherPlayer.StartupEntries.Count > 0) problems.AddRange(OtherPlayerDetector.Stop(otherPlayer, false));
        }
        if (problems.Count > 0) MessageBox.Show(string.Join("\n\n", problems), "WinSqueeze", MessageBoxButton.OK, MessageBoxImage.Warning);
        if (otherPlayers.Count > 0) await Task.Delay(500); // let Windows release the device
    }

    // Hot-plug: Windows broadcasts WM_DEVICECHANGE to top-level windows; debounce the burst
    IntPtr WindowMessageHook(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WM_DEVICECHANGE && wParam.ToInt32() == DBT_DEVNODES_CHANGED) { deviceChangeTimer.Stop(); deviceChangeTimer.Start(); }
        if (message == WM_HOTKEY) { handled = true; _ = OnGlobalHotkeyAsync(wParam.ToInt32()); }
        return IntPtr.Zero;
    }

    public async Task RefreshDevicesAsync()
    {
        try { Devices = await OutputDeviceList.ListAsync(); } catch (Exception listError) { Devices = new(); Player.SetWaiting(L.T("Could not list output devices: {error}", ("error", listError.Message))); }
        DevicesChanged?.Invoke();
    }

    async Task OnDevicesChangedAsync()
    {
        await RefreshDevicesAsync();
        var profile = Settings.ActiveProfile;
        if (profile == null) return;
        var preferredDevice = profile.DeviceName == "" ? DefaultDevice() : FindDevice(profile.DeviceName, profile.DeviceHostApi);
        var currentPresent = Player.Device != null && FindDevice(Player.Device.Name, Player.Device.HostApi) != null;
        // Restart when waiting and the device is back, when the device vanished, when the preferred device returns while on the fallback, or when Windows' default moved
        if (Player.State is PlayerState.WaitingForDevice or PlayerState.Failed && preferredDevice != null) await StartPlayerAsync(false);
        else if (Player.State == PlayerState.Running && !currentPresent) await StartPlayerAsync(false);
        else if (UsingFallbackDevice && preferredDevice != null) await StartPlayerAsync(true);
        else if (Player.State == PlayerState.Running && preferredDevice != null && Player.Device != null && !preferredDevice.Matches(Player.Device.Name, Player.Device.HostApi)) await StartPlayerAsync(true);
    }

    public OutputDevice? FindDevice(string deviceName, string hostApi) => deviceName == "" ? null : Devices.FirstOrDefault(device => device.Matches(deviceName, hostApi));

    // Windows' default output as Squeezelite names it: its WASAPI endpoint (so exclusive mode still works), else the MME Sound Mapper, which follows the default by itself
    public OutputDevice? DefaultDevice()
    {
        var defaultName = windowsDefault.Name;
        if (defaultName == "") return null;
        return Devices.FirstOrDefault(device => device.Name == defaultName && device.Driver == "WASAPI") ?? Devices.FirstOrDefault(device => device.Name == defaultName) ?? Devices.FirstOrDefault(device => device.Name.StartsWith("Microsoft Sound Mapper"));
    }

    public async Task StartPlayerAsync(bool resumePosition)
    {
        if (!IsOffline) _ = Library.RefreshAsync(false);
        var profile = Settings.ActiveProfile;
        Downloads.Open(profile); History.Open(profile);
        if (profile is not { Host.Length: > 0 }) { Player.SetWaiting(L.T("No server set. Open Settings › Servers.")); return; }
        // No device chosen: play to the Windows default
        var outputDevice = profile.DeviceName == "" ? DefaultDevice() : FindDevice(profile.DeviceName, profile.DeviceHostApi);
        if (outputDevice == null && profile.DeviceName == "") { Player.SetWaiting(L.T("No output device found. Waiting for one.")); return; }
        UsingFallbackDevice = false;
        if (outputDevice == null)
        {
            outputDevice = FindDevice(Settings.Audio.FallbackDeviceName, Settings.Audio.FallbackDeviceHostApi);
            if (outputDevice == null) { Player.SetWaiting(L.T("{device} is not connected. Waiting for it to return.", ("device", profile.DeviceName))); trayIcon.ShowTip(L.T("Output device missing"), L.T("{device} is not connected. WinSqueeze will start playing to it when it returns.", ("device", profile.DeviceName))); return; }
            UsingFallbackDevice = true;
            trayIcon.ShowTip(L.T("Using fallback output"), L.T("{device} is not connected, so WinSqueeze is using {fallback}.", ("device", profile.DeviceName), ("fallback", outputDevice.Name)));
        }
        (string mode, double seconds) resumeState = ("", 0);
        if (resumePosition && Player.State == PlayerState.Running) resumeState = await ReadPlaybackStateAsync();
        // First start of this run: read what the server is doing before Squeezelite connects, then start it held (silent)
        var checkStartup = !startupPlaybackChecked; startupPlaybackChecked = true;
        (string mode, double seconds) serverBefore = checkStartup ? await ReadPlaybackStateAsync() : ("", 0);
        Recorder.Flush(); // finish the file cleanly; it carries on in a new one once Squeezelite is back
        Damaged.Reset();
        Player.Start(Offline is { } offlineServer ? $"127.0.0.1:{offlineServer.SlimPort}" : profile.Host, outputDevice, Settings.Audio, Settings.PlayerMac, checkStartup);
        if (SqueezeliteHost.SupportsRecording) Recorder.Start(Settings.PlayerMac);
        Visualiser.Tuning = Settings.Meters;
        if (SqueezeliteHost.SupportsVisualiser) Visualiser.Start(Settings.PlayerMac, () => Player.OutputLatencyMs > 0 ? Player.OutputLatencyMs : Settings.Audio.LatencyMs);
        Equaliser.Tuning = Settings.Equaliser; Equaliser.CurrentRate = () => Visualiser.SampleRate;
        if (SqueezeliteHost.SupportsEqualiser) Equaliser.Start(Settings.PlayerMac);
        if (resumeState.mode == "play") await ResumeAtAsync(resumeState.seconds);
        if (checkStartup) _ = SettleStartupPlaybackAsync(serverBefore.mode == "play", serverBefore.seconds);
    }

    // Lyrion restarts whatever a reconnecting player was playing (SPEC.md › Windows integration). Squeezelite starts held, so nothing is heard
    // until this decides: carry on only if "Resume playing on startup" is ticked and the server was playing to this player
    bool startupPlaybackChecked, restartedForUpdate;
    async Task SettleStartupPlaybackAsync(bool serverWasPlaying, double seconds)
    {
        try
        {
            if (!await WaitForPlayerConnectedAsync()) return;
            if (!restartedForUpdate && Settings.General.ResumeOnStartup && serverWasPlaying)
            {
                SqueezeliteHost.ReleaseHold(Settings.PlayerMac);
                if ((await ReadPlaybackStateAsync()).mode != "play") await ResumeAtAsync(seconds);
                return;
            }
            for (var check = 0; check < 6; check++) // the server decides as the player connects; look a few times in case it's slow
            {
                if ((await ReadPlaybackStateAsync()).mode == "play")
                {
                    await ServerRequestAsync(Settings.PlayerMac, "pause", "1"); // keeps the queue and position
                    for (var wait = 0; wait < 10 && (await ReadPlaybackStateAsync()).mode == "play"; wait++) await Task.Delay(200);
                    if ((await ReadPlaybackStateAsync()).mode == "play") await ServerRequestAsync(Settings.PlayerMac, "stop");
                    await Task.Delay(300); // let Squeezelite act on it before the hold lifts
                    return;
                }
                await Task.Delay(250);
            }
        }
        catch (Exception error) { CrashReporter.Write("Startup playback check failed", error, false); }
        finally { SqueezeliteHost.ReleaseHold(Settings.PlayerMac); }
    }

    async Task<bool> WaitForPlayerConnectedAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(500);
            try { if (LyrionClient.ReadString(await ServerRequestAsync(Settings.PlayerMac, "connected", "?"), "_connected") == "1") return true; } catch { }
        }
        return false;
    }

    async Task<(string mode, double seconds)> ReadPlaybackStateAsync()
    {
        try
        {
            var status = await ServerRequestAsync(Settings.PlayerMac, "status", "-", 1);
            return (LyrionClient.ReadString(status, "mode"), LyrionClient.ReadDouble(status, "time"));
        }
        catch { return ("", 0); }
    }

    // After a restart, wait for the player to reconnect, then continue from the same position
    async Task ResumeAtAsync(double seconds)
    {
        if (!await WaitForPlayerConnectedAsync()) return;
        try { await ServerRequestAsync(Settings.PlayerMac, "play"); await ServerRequestAsync(Settings.PlayerMac, "time", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)); } catch { }
    }

    // Every player and server command goes through here, so offline the same commands reach the offline server
    public async Task<JsonElement> ServerRequestAsync(string playerId, params object[] command)
    {
        if (Offline is { } offlineServer) return offlineServer.Request(command);
        using var lyrionClient = new LyrionClient(Settings.ActiveProfile!);
        return await lyrionClient.RequestAsync(playerId, command);
    }

    // ---- Offline (SPEC.md › Offline downloads) ----
    readonly DispatcherTimer serverBackTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    public async Task GoOfflineAsync(bool automatic)
    {
        if (IsOffline || Settings.ActiveProfile is not { } profile) return;
        if (!Downloads.HasDownloads) { if (!automatic) ShowTip(L.T("Nothing downloaded yet"), L.T("Download albums from any selection (Download) to play them without the server.")); return; }
        // Carry on with the downloaded part of the current queue, at the same spot
        (List<long> ids, int index, double seconds, bool playing) carried = (new(), 0, 0, false);
        if (!automatic) try
        {
            var status = await ServerRequestAsync(Settings.PlayerMac, "status", 0, 500);
            if (status.ValueKind == JsonValueKind.Object && status.TryGetProperty("playlist_loop", out var playlistLoop)) carried = (playlistLoop.EnumerateArray().Select(item => LyrionClient.ReadLong(item, "id")).ToList(), LyrionClient.ReadInt(status, "playlist_cur_index"), LyrionClient.ReadDouble(status, "time"), LyrionClient.ReadString(status, "mode") == "play");
        }
        catch { }
        Offline = new Offline.OfflineServer(Downloads.Find) { PlayerMac = Settings.PlayerMac, PlayerName = Settings.Audio.PlayerName };
        Offline.Changed += () => Dispatcher.BeginInvoke(async () => await Session.PollAsync());
        Offline.Log += line => CrashReporter.Note("offline: " + line);
        Session.SelectedPlayerId = ""; ServerBack = false;
        await StartPlayerAsync(false);
        serverBackTimer.Start();
        if (automatic) ShowTip(L.T("Playing offline"), L.T("The server can't be reached, so WinSqueeze is playing your downloads. It goes back online by itself when the server returns and nothing is playing."));
        if (carried.ids.Count > 0)
        {
            var downloadedIds = Downloads.DownloadedIds().ToHashSet();
            var keptIds = carried.ids.Where(downloadedIds.Contains).ToList();
            if (keptIds.Count > 0)
            {
                for (var attempt = 0; attempt < 20 && !Offline.Connected; attempt++) await Task.Delay(250);
                Offline.Request(new object[] { "playlistcontrol", "cmd:load", "track_id:" + string.Join(",", keptIds) });
                var currentId = carried.ids.ElementAtOrDefault(carried.index);
                if (keptIds.IndexOf(currentId) is var keptIndex and > 0) Offline.Request(new object[] { "playlist", "index", keptIndex });
                if (keptIds.Contains(currentId) && carried.seconds > 1) Offline.Request(new object[] { "time", carried.seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                if (!carried.playing) Offline.Request(new object[] { "pause", "1" });
            }
        }
        StatusChanged?.Invoke();
    }

    public async Task GoOnlineAsync()
    {
        if (Offline is not { } offlineServer) return;
        serverBackTimer.Stop();
        Offline = null; ServerBack = false;
        await StartPlayerAsync(false); // Squeezelite back to the server, which still has this player's own queue
        offlineServer.Dispose();
        StatusChanged?.Invoke();
    }

    // Offline: is the server back? Go back by itself when nothing is playing, otherwise offer it
    async Task CheckServerBackAsync()
    {
        if (Offline is not { } offlineServer || Settings.ActiveProfile is not { Host.Length: > 0 } profile) return;
        try { using var lyrionClient = new LyrionClient(profile); await lyrionClient.GetServerStatusAsync(); }
        catch { if (ServerBack) { ServerBack = false; StatusChanged?.Invoke(); } return; }
        if (!offlineServer.IsPlaying) { await GoOnlineAsync(); ShowTip(L.T("Back online"), L.T("The server is reachable again.")); }
        else if (!ServerBack) { ServerBack = true; StatusChanged?.Invoke(); }
    }

    public async Task SendPlayerCommandAsync(params object[] command)
    {
        if (Settings.ActiveProfile is not { Host.Length: > 0 }) return;
        try { await ServerRequestAsync(Settings.PlayerMac, command); }
        catch (Exception commandError) { trayIcon.ShowTip(L.T("Server not reachable"), commandError.Message); }
    }

    public async Task SelectDeviceAsync(OutputDevice device)
    {
        var profile = Settings.ActiveProfile;
        if (profile == null) return;
        profile.DeviceName = device.IsWindowsDefault ? "" : device.Name; profile.DeviceHostApi = device.IsWindowsDefault ? "" : device.HostApi;
        Settings.Save();
        await StartPlayerAsync(true);
    }

    void OnPlayerStatusChanged() { trayIcon.Refresh(); StatusChanged?.Invoke(); }

    // The window shows its own message; hidden in the tray, a tip says it instead
    void OnDamagedFound(DamagedFiles.DamagedFile damagedFile, string what)
    {
        DamagedFound?.Invoke(damagedFile, what);
        if (mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized) return;
        var heading = what switch { "skipped" => L.T("Skipped a damaged file"), "unplayable" => L.T("Can't play a damaged file"), _ => L.T("This file is damaged") };
        trayIcon.ShowTip(heading, $"{damagedFile.Title} · {damagedFile.Artist}. " + L.T("Library health lists damaged files."));
    }

    public string StatusJson() => JsonSerializer.Serialize(new
    {
        type = "status",
        server = Settings.ActiveProfile?.ToString() ?? "",
        state = Player.State.ToString(),
        message = Player.StatusText,
        device = Player.Device?.Name ?? "",
        driver = Player.Device?.Driver ?? "",
        exclusive = Player.ExclusiveActive,
        eqActive = Settings.Equaliser.Enabled && SqueezeliteHost.SupportsEqualiser,
        replayGain = OwnReplayGainMode != "0",
        transitions = OwnTransitionType != "0",
        sampleRate = Player.OutputSampleRate,
        recording = new { available = SqueezeliteHost.SupportsRecording && Settings.Recording.ShowButton, on = Recorder.Recording, file = System.IO.Path.GetFileName(Recorder.CurrentFile), startedAt = Recorder.Recording ? new DateTimeOffset(Recorder.StartedAt).ToUnixTimeMilliseconds() : 0, files = Recorder.FilesThisRecording, note = Recorder.Note, noteSaved = Recorder.NoteIsSaved, noteId = Recorder.NoteId, folder = Recorder.SessionFolder },
        fallback = UsingFallbackDevice,
        offline = IsOffline,
        serverBack = ServerBack,
        version = Updater.CurrentVersion,
        updateReady = Updater.ReadyVersion
    });

    public async Task ReplaceSettingsAsync(AppSettings importedSettings)
    {
        Settings = importedSettings;
        Settings.Save();
        StartupRegistration.Apply(Settings.General.StartWithWindows);
        ApplyTrayIcon();
        await StartPlayerAsync(false);
        LibraryChanged?.Invoke();
    }

    public void NotifyLibraryChanged() => LibraryChanged?.Invoke();
    public event Action? HintsChanged;
    public void NotifyHintsChanged() => HintsChanged?.Invoke();
    public event Action? TrackInfoChanged;
    public void NotifyTrackInfoChanged() { TrackInfo.Forget(); TrackInfoChanged?.Invoke(); }

    public void ShowMainWindow() { mainWindow.Show(); if (mainWindow.WindowState == WindowState.Minimized) mainWindow.WindowState = WindowState.Normal; mainWindow.Activate(); }
    // Settings › General › Show system tray icon: without it the window never hides, it minimises to the taskbar
    public void ApplyTrayIcon()
    {
        trayIcon.Visible = Settings.General.ShowTrayIcon;
        if (!Settings.General.ShowTrayIcon && !mainWindow.IsVisible) { mainWindow.WindowState = WindowState.Minimized; mainWindow.Show(); }
    }
    public void ToggleMainWindow() { if (mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized) mainWindow.Hide(); else ShowMainWindow(); }
    public void ShowTip(string title, string text) => trayIcon.ShowTip(title, text);

    public void OpenSettings(string tabName = "")
    {
        // A window that fails to build says so, instead of the click doing nothing
        if (settingsWindow == null) { try { settingsWindow = new SettingsWindow(); } catch (Exception error) { CrashReporter.Write("Settings couldn't open", error, false); ShowTip(L.T("Settings couldn't open"), L.T("Please tell us on GitHub Discussions. The details are in crash.log.")); return; } settingsWindow.Closed += (sender, args) => settingsWindow = null; }
        if (mainWindow.IsVisible && settingsWindow.Owner == null) settingsWindow.Owner = mainWindow; // keeps Settings in front of the main window
        settingsWindow.SelectTab(tabName);
        settingsWindow.Show(); settingsWindow.Activate();
    }

    public void Quit()
    {
        if (IsQuitting) return;
        IsQuitting = true;
        // The process always ends: a copy left running would keep its WebView2 processes and block the next launch
        new Thread(() => { Thread.Sleep(5000); Environment.Exit(0); }) { IsBackground = true, Name = "Quit watchdog" }.Start();
        // Recorder first: finish any recording before Squeezelite stops
        foreach (var quitStep in new Action[] { () => { mainWindow.SaveBounds(); Settings.Save(); }, Recorder.Dispose, Player.Dispose, windowsDefault.Dispose, Downloads.Dispose, () => Offline?.Dispose(), trayIcon.Dispose, () => settingsWindow?.Close(), () => lyricsWindow?.Close(), mainWindow.Close })
            try { quitStep(); } catch (Exception error) { CrashReporter.Write("Quit step failed", error, false); }
        Shutdown();
    }
}
