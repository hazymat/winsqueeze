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
    public event Action? HistoryChanged;
    public Offline.OfflineServer? Offline { get; private set; } // set while playing downloads without the server
    public bool IsOffline => Offline != null;
    public bool ServerBack { get; private set; } // offline, and the server answers again
    EqualiserWindow? equaliserWindow;
    public List<OutputDevice> Devices { get; private set; } = new();
    public bool UsingFallbackDevice { get; private set; }
    public bool IsQuitting { get; private set; }
    public event Action? DevicesChanged;
    public event Action? StatusChanged;
    public event Action? LibraryChanged;
    public event Action? DownloadsChanged;

    MainWindow mainWindow = null!;
    TrayIcon trayIcon = null!;
    SettingsWindow? settingsWindow;
    Mutex? singleInstanceMutex;
    EventWaitHandle? showWindowSignal;
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

    // Read-only fidelity check (SPEC.md › Playback and fidelity): warn if Lyrion caps this player's bitrate, which forces transcoding. Never changes it
    async Task CheckServerTranscodingAsync()
    {
        await Task.Delay(8000); // give the player time to register with the server
        try
        {
            if (Settings.ActiveProfile is not { Host.Length: > 0 } profile) return;
            using var lyrionClient = new LyrionClient(profile);
            var limit = LyrionClient.ReadString(await lyrionClient.RequestAsync(Settings.PlayerMac, "playerpref", "maxBitrate", "?"), "_p2");
            if (limit is not ("" or "0")) trayIcon.ShowTip("The server is transcoding", $"Lyrion limits {Settings.Audio.PlayerName} to {limit} kbps, so music isn't lossless. In Lyrion: Settings › Player › Audio › Bitrate limiting › No limit.");
        }
        catch { }
    }

    // ---- Tray actions ----
    public void SetWindowMode(string mode) { ShowMainWindow(); mainWindow.SetWindowMode(mode); }
    public void NewPlaylist() { ShowMainWindow(); mainWindow.PushNewPlaylist(); }
    public void SetSkin(string skin) { Settings.General.Skin = skin; Settings.Save(); ApplySkin(); }
    public async Task SelectPlayerAsync(string playerId) { Session.SelectedPlayerId = playerId == Settings.PlayerMac ? "" : playerId; await Session.RequestAsync(true, "status", "-", 1); }

    protected override async void OnStartup(StartupEventArgs startupArgs)
    {
        singleInstanceMutex = new Mutex(true, "WinSqueeze.SingleInstance", out var isFirstInstance);
        showWindowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "WinSqueeze.ShowWindow");
        if (!isFirstInstance) { showWindowSignal.Set(); Shutdown(); return; } // second launch just shows the running app
        ThreadPool.RegisterWaitForSingleObject(showWindowSignal, (state, timedOut) => Dispatcher.BeginInvoke(ShowMainWindow), null, -1, false);
        if (AppPaths.IsPortable) AppUpdater.CleanUpAfterPortableUpdate();
        CrashReporter.Install(this);
        Settings = AppSettings.Load();
        mainWindow = new MainWindow();
        trayIcon = new TrayIcon();
        Player.StatusChanged += () => Dispatcher.BeginInvoke(OnPlayerStatusChanged);
        HwndSource.FromHwnd(new WindowInteropHelper(mainWindow).EnsureHandle()).AddHook(WindowMessageHook);
        deviceChangeTimer.Tick += async (sender, args) => { deviceChangeTimer.Stop(); await OnDevicesChangedAsync(); };
        // A restart for an update brings the window back exactly as it was (--window=normal|maximised|minimised|hidden,left,top,width,height); otherwise the start-minimised setting applies
        mainWindow.RestoreBoundsForDisplays();
        var windowArgument = startupArgs.Args.FirstOrDefault(argument => argument.StartsWith("--window="));
        if (windowArgument != null) mainWindow.RestoreFromArgument(windowArgument["--window=".Length..]);
        else if (!(Settings.General.StartMinimised || startupArgs.Args.Contains("--minimised"))) mainWindow.Show();
        if (Settings.ActiveProfile is not { Host.Length: > 0 }) OpenSettings("Servers");
        Updater.Changed += () => Dispatcher.BeginInvoke(() => StatusChanged?.Invoke());
        Updater.Start();
        RefreshIconsAfterUpdate();
        if (CrashReporter.TakeUnseenReport()) ShowTip("WinSqueeze had a problem last time", "It crashed or stopped responding. Details are in Settings › About › Open logs (crash.log).");
        try { Media = new MediaControls(); } catch { } // SMTC needs Windows 10 1903+
        Session.StatusUpdated += () => { if (Settings.ActiveProfile is { } profile) Media?.Update(Session.LastStatus, $"http://{profile.Host}:{profile.Port}"); trayIcon.RefreshNowPlaying(Session.LastStatus); NotifyTrackChange(Session.LastStatus); History.OnStatus(Session.LastStatus); Recorder.OnStatus(Session.LastStatus, Session.SelectedPlayerId == ""); };
        Recorder.Changed += () => Dispatcher.BeginInvoke(() => StatusChanged?.Invoke());
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
        await StartPlayerAsync(false);
        _ = CheckServerTranscodingAsync();
        // After an update or version switch, carry on from where playback was
        var resumeArgument = startupArgs.Args.FirstOrDefault(argument => argument.StartsWith("--resume="));
        if (resumeArgument != null && Player.State == PlayerState.Running && double.TryParse(resumeArgument["--resume=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var resumeSeconds)) await ResumeAtAsync(resumeSeconds);
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
    // After an update: the release notes of every version since the one that last ran (including skipped ones)
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
            if (releases.Count > 0) mainWindow.PushWhatsNew(previousVersion, currentVersion, releases);
        }
        catch (Exception error) { CrashReporter.Write("What's new: release notes unavailable", error, false); }
    }

    public void RaiseStatusChanged() => StatusChanged?.Invoke();
    // The EQ floats beside the main window (docks to its edges); opening again brings it forward
    public void OpenEqualiser()
    {
        if (equaliserWindow == null) { equaliserWindow = new EqualiserWindow(mainWindow); equaliserWindow.Closed += (sender, args) => equaliserWindow = null; }
        equaliserWindow.Show(); equaliserWindow.Activate();
    }
    public void ToggleEqualiser() { if (equaliserWindow is { IsVisible: true }) equaliserWindow.Close(); else OpenEqualiser(); } // the transport's analyser opens and closes it
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
        singleInstanceMutex?.ReleaseMutex(); singleInstanceMutex?.Dispose(); singleInstanceMutex = null;
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
            var startupText = otherPlayer.StartupEntries.Count == 0 ? "" : $"\n\nIt starts at login via: {string.Join(", ", otherPlayer.StartupEntries.Select(entry => entry.Description))}.";
            var choice = otherAction == OtherSqueezeliteAction.Stop ? MessageBoxResult.Yes : MessageBox.Show(
                $"Another Squeezelite is running:\n{otherPlayer.ExePath} (process {otherPlayer.ProcessId})\n\nIt can hold your output device and shows up as a second player on the server.{startupText}\n\n" +
                (otherPlayer.StartupEntries.Count == 0 ? "Stop it now?" : "Yes: stop it and switch off its startup entry (moved aside, not deleted).\nNo: stop it this time only.\nCancel: leave it running."),
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
        try { Devices = await OutputDeviceList.ListAsync(); } catch (Exception listError) { Devices = new(); Player.SetWaiting($"Could not list output devices: {listError.Message}"); }
        DevicesChanged?.Invoke();
    }

    async Task OnDevicesChangedAsync()
    {
        await RefreshDevicesAsync();
        var profile = Settings.ActiveProfile;
        if (profile == null) return;
        var preferredPresent = FindDevice(profile.DeviceName, profile.DeviceHostApi) != null;
        var currentPresent = Player.Device != null && FindDevice(Player.Device.Name, Player.Device.HostApi) != null;
        // Restart when waiting and the device is back, when the device vanished, or when the preferred device returns while on the fallback
        if (Player.State is PlayerState.WaitingForDevice or PlayerState.Failed && preferredPresent) await StartPlayerAsync(false);
        else if (Player.State == PlayerState.Running && !currentPresent) await StartPlayerAsync(false);
        else if (UsingFallbackDevice && preferredPresent) await StartPlayerAsync(true);
    }

    public OutputDevice? FindDevice(string deviceName, string hostApi) => deviceName == "" ? null : Devices.FirstOrDefault(device => device.Matches(deviceName, hostApi));

    public async Task StartPlayerAsync(bool resumePosition)
    {
        if (!IsOffline) _ = Library.RefreshAsync(false);
        var profile = Settings.ActiveProfile;
        Downloads.Open(profile); History.Open(profile);
        if (profile is not { Host.Length: > 0 }) { Player.SetWaiting("No server set. Open Settings › Servers."); return; }
        if (profile.DeviceName == "") { Player.SetWaiting("Choose an output device in Settings › Audio."); return; }
        var outputDevice = FindDevice(profile.DeviceName, profile.DeviceHostApi);
        UsingFallbackDevice = false;
        if (outputDevice == null)
        {
            outputDevice = FindDevice(Settings.Audio.FallbackDeviceName, Settings.Audio.FallbackDeviceHostApi);
            if (outputDevice == null) { Player.SetWaiting($"{profile.DeviceName} is not connected. Waiting for it to return."); trayIcon.ShowTip("Output device missing", $"{profile.DeviceName} is not connected. WinSqueeze will start playing to it when it returns."); return; }
            UsingFallbackDevice = true;
            trayIcon.ShowTip("Using fallback output", $"{profile.DeviceName} is not connected, so WinSqueeze is using {outputDevice.Name}.");
        }
        (string mode, double seconds) resumeState = ("", 0);
        if (resumePosition && Player.State == PlayerState.Running) resumeState = await ReadPlaybackStateAsync();
        Recorder.Flush(); // finish the file cleanly; it carries on in a new one once Squeezelite is back
        Player.Start(Offline is { } offlineServer ? $"127.0.0.1:{offlineServer.SlimPort}" : profile.Host, outputDevice, Settings.Audio, Settings.PlayerMac);
        if (SqueezeliteHost.SupportsRecording) Recorder.Start(Settings.PlayerMac);
        Visualiser.Tuning = Settings.Meters;
        if (SqueezeliteHost.SupportsVisualiser) Visualiser.Start(Settings.PlayerMac, Settings.Audio.LatencyMs);
        Equaliser.Tuning = Settings.Equaliser; Equaliser.CurrentRate = () => Visualiser.SampleRate;
        if (SqueezeliteHost.SupportsEqualiser) Equaliser.Start(Settings.PlayerMac);
        if (resumeState.mode == "play") await ResumeAtAsync(resumeState.seconds);
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
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(500);
            try
            {
                var connected = await ServerRequestAsync(Settings.PlayerMac, "connected", "?");
                if (LyrionClient.ReadString(connected, "_connected") != "1") continue;
                await ServerRequestAsync(Settings.PlayerMac, "play");
                await ServerRequestAsync(Settings.PlayerMac, "time", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            catch { }
        }
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
        if (!Downloads.HasDownloads) { if (!automatic) ShowTip("Nothing downloaded yet", "Download albums from any selection (Download) to play them without the server."); return; }
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
        if (automatic) ShowTip("Playing offline", "The server can't be reached, so WinSqueeze is playing your downloads. It goes back online by itself when the server returns and nothing is playing.");
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
        if (!offlineServer.IsPlaying) { await GoOnlineAsync(); ShowTip("Back online", "The server is reachable again."); }
        else if (!ServerBack) { ServerBack = true; StatusChanged?.Invoke(); }
    }

    public async Task SendPlayerCommandAsync(params object[] command)
    {
        if (Settings.ActiveProfile is not { Host.Length: > 0 }) return;
        try { await ServerRequestAsync(Settings.PlayerMac, command); }
        catch (Exception commandError) { trayIcon.ShowTip("Server not reachable", commandError.Message); }
    }

    public async Task SelectDeviceAsync(OutputDevice device)
    {
        var profile = Settings.ActiveProfile;
        if (profile == null) return;
        profile.DeviceName = device.Name; profile.DeviceHostApi = device.HostApi;
        Settings.Save();
        await StartPlayerAsync(true);
    }

    void OnPlayerStatusChanged() { trayIcon.Refresh(); StatusChanged?.Invoke(); }

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
        sampleRate = Player.OutputSampleRate,
        recording = new { available = SqueezeliteHost.SupportsRecording && Settings.Recording.ShowButton, on = Recorder.Recording, file = System.IO.Path.GetFileName(Recorder.CurrentFile), startedAt = Recorder.Recording ? new DateTimeOffset(Recorder.StartedAt).ToUnixTimeMilliseconds() : 0, files = Recorder.FilesThisRecording, note = Recorder.Note, noteId = Recorder.NoteId, folder = Recorder.SessionFolder },
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
        await StartPlayerAsync(false);
        LibraryChanged?.Invoke();
    }

    public void NotifyLibraryChanged() => LibraryChanged?.Invoke();
    public event Action? HintsChanged;
    public void NotifyHintsChanged() => HintsChanged?.Invoke();

    public void ShowMainWindow() { mainWindow.Show(); if (mainWindow.WindowState == WindowState.Minimized) mainWindow.WindowState = WindowState.Normal; mainWindow.Activate(); }
    public void ToggleMainWindow() { if (mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized) mainWindow.Hide(); else ShowMainWindow(); }
    public void ShowTip(string title, string text) => trayIcon.ShowTip(title, text);

    public void OpenSettings(string tabName = "")
    {
        if (settingsWindow == null) { settingsWindow = new SettingsWindow(); settingsWindow.Closed += (sender, args) => settingsWindow = null; }
        if (mainWindow.IsVisible && settingsWindow.Owner == null) settingsWindow.Owner = mainWindow; // keeps Settings in front of the main window
        settingsWindow.SelectTab(tabName);
        settingsWindow.Show(); settingsWindow.Activate();
    }

    public void Quit()
    {
        mainWindow.SaveBounds(); Settings.Save();
        IsQuitting = true;
        Recorder.Dispose(); // finish any recording before Squeezelite stops
        Player.Dispose();
        Downloads.Dispose(); Offline?.Dispose();
        trayIcon.Dispose();
        settingsWindow?.Close();
        mainWindow.Close();
        Shutdown();
    }
}
