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
    public List<OutputDevice> Devices { get; private set; } = new();
    public bool UsingFallbackDevice { get; private set; }
    public bool IsQuitting { get; private set; }
    public event Action? DevicesChanged;
    public event Action? StatusChanged;
    public event Action? LibraryChanged;

    MainWindow mainWindow = null!;
    TrayIcon trayIcon = null!;
    SettingsWindow? settingsWindow;
    Mutex? singleInstanceMutex;
    EventWaitHandle? showWindowSignal;
    readonly DispatcherTimer deviceChangeTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    const int WM_DEVICECHANGE = 0x0219, DBT_DEVNODES_CHANGED = 0x0007;

    protected override async void OnStartup(StartupEventArgs startupArgs)
    {
        singleInstanceMutex = new Mutex(true, "WinSqueeze.SingleInstance", out var isFirstInstance);
        showWindowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "WinSqueeze.ShowWindow");
        if (!isFirstInstance) { showWindowSignal.Set(); Shutdown(); return; } // second launch just shows the running app
        ThreadPool.RegisterWaitForSingleObject(showWindowSignal, (state, timedOut) => Dispatcher.BeginInvoke(ShowMainWindow), null, -1, false);
        if (AppPaths.IsPortable) AppUpdater.CleanUpAfterPortableUpdate();
        Settings = AppSettings.Load();
        mainWindow = new MainWindow();
        trayIcon = new TrayIcon();
        Player.StatusChanged += () => Dispatcher.BeginInvoke(OnPlayerStatusChanged);
        HwndSource.FromHwnd(new WindowInteropHelper(mainWindow).EnsureHandle()).AddHook(WindowMessageHook);
        deviceChangeTimer.Tick += async (sender, args) => { deviceChangeTimer.Stop(); await OnDevicesChangedAsync(); };
        if (!(Settings.General.StartMinimised || startupArgs.Args.Contains("--minimised"))) mainWindow.Show();
        if (Settings.ActiveProfile is not { Host.Length: > 0 }) OpenSettings("Servers");
        Updater.Changed += () => Dispatcher.BeginInvoke(() => StatusChanged?.Invoke());
        Updater.Start();
        RefreshIconsAfterUpdate();
        try { Media = new MediaControls(); } catch { } // SMTC needs Windows 10 1903+
        Session.StatusUpdated += () => { if (Settings.ActiveProfile is { } profile) Media?.Update(Session.LastStatus, $"http://{profile.Host}:{profile.Port}"); trayIcon.RefreshNowPlaying(Session.LastStatus); };
        Session.Start();
        Library.Start();
        await CheckOtherPlayersAsync();
        await RefreshDevicesAsync();
        await StartPlayerAsync(false);
        // After an update or version switch, carry on from where playback was
        var resumeArgument = startupArgs.Args.FirstOrDefault(argument => argument.StartsWith("--resume="));
        if (resumeArgument != null && Player.State == PlayerState.Running && double.TryParse(resumeArgument["--resume=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var resumeSeconds)) await ResumeAtAsync(resumeSeconds);
    }

    // Windows caches shortcut and taskbar icons; after a version change, ask the shell to refresh them so a new icon shows up
    [System.Runtime.InteropServices.DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    void RefreshIconsAfterUpdate()
    {
        if (Settings.General.LastRunVersion == Updater.CurrentVersion) return;
        Settings.General.LastRunVersion = Updater.CurrentVersion; Settings.Save();
        try { SHChangeNotify(0x08000000 /*SHCNE_ASSOCCHANGED*/, 0, IntPtr.Zero, IntPtr.Zero); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ie4uinit.exe", "-show") { CreateNoWindow = true, UseShellExecute = false })?.Dispose(); } catch { }
    }

    public void ApplyWindowStyle() => mainWindow.ApplyWindowStyle();

    public async Task<string[]> ResumeArgumentsAsync()
    {
        (string mode, double seconds) playbackState = Player.State == PlayerState.Running ? await ReadPlaybackStateAsync() : ("", 0);
        return playbackState.mode == "play" ? new[] { "--resume=" + playbackState.seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) } : Array.Empty<string>();
    }

    // Stops Squeezelite and releases the single-instance lock so the new version can start straight away
    public void PrepareForRestart()
    {
        IsQuitting = true;
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
        _ = Library.RefreshAsync(false);
        var profile = Settings.ActiveProfile;
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
        Player.Start(profile, outputDevice, Settings.Audio, Settings.PlayerMac);
        if (resumeState.mode == "play") await ResumeAtAsync(resumeState.seconds);
    }

    async Task<(string mode, double seconds)> ReadPlaybackStateAsync()
    {
        try
        {
            using var lyrionClient = new LyrionClient(Settings.ActiveProfile!);
            var status = await lyrionClient.RequestAsync(Settings.PlayerMac, "status", "-", 1);
            return (LyrionClient.ReadString(status, "mode"), LyrionClient.ReadDouble(status, "time"));
        }
        catch { return ("", 0); }
    }

    // After a restart, wait for the player to reconnect, then continue from the same position
    async Task ResumeAtAsync(double seconds)
    {
        using var lyrionClient = new LyrionClient(Settings.ActiveProfile!);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(500);
            try
            {
                var connected = await lyrionClient.RequestAsync(Settings.PlayerMac, "connected", "?");
                if (LyrionClient.ReadString(connected, "_connected") != "1") continue;
                await lyrionClient.RequestAsync(Settings.PlayerMac, "play");
                await lyrionClient.RequestAsync(Settings.PlayerMac, "time", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            catch { }
        }
    }

    public async Task SendPlayerCommandAsync(params object[] command)
    {
        if (Settings.ActiveProfile is not { Host.Length: > 0 } profile) return;
        try { using var lyrionClient = new LyrionClient(profile); await lyrionClient.RequestAsync(Settings.PlayerMac, command); }
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
        sampleRate = Player.OutputSampleRate,
        fallback = UsingFallbackDevice,
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
        IsQuitting = true;
        Player.Dispose();
        trayIcon.Dispose();
        settingsWindow?.Close();
        mainWindow.Close();
        Shutdown();
    }
}
