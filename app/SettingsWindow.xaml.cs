using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WinSqueeze.Audio;
using WinSqueeze.Server;
using WinSqueeze.Settings;
namespace WinSqueeze;

// Changes apply immediately (no OK/Apply), SPEC.md › Settings
public partial class SettingsWindow : Window
{
    static readonly OutputDevice noFallback = new(-1, "None: wait for the chosen device", "");
    App App => App.Instance;
    AppSettings Settings => App.Settings;
    bool loadingControls;

    public SettingsWindow()
    {
        InitializeComponent();
        WireGeneralTab(); WireServersTab(); WireAudioTab(); WireAboutTab();
        App.DevicesChanged += LoadDevices;
        App.StatusChanged += ShowPlayerStatus;
        Closed += (sender, args) => { App.DevicesChanged -= LoadDevices; App.StatusChanged -= ShowPlayerStatus; };
        LoadDevices(); ShowPlayerStatus();
    }

    public void SelectTab(string tabName)
    {
        var tabByName = new Dictionary<string, TabItem> { ["General"] = generalTab, ["Servers"] = serversTab, ["Audio"] = audioTab, ["About"] = aboutTab };
        if (tabByName.TryGetValue(tabName, out var selectedTab)) settingsTabs.SelectedItem = selectedTab;
    }

    void WireGeneralTab()
    {
        startWithWindowsBox.IsChecked = Settings.General.StartWithWindows;
        startMinimisedBox.IsChecked = Settings.General.StartMinimised;
        closeHidesBox.IsChecked = Settings.General.CloseHidesToTray;
        startWithWindowsBox.Click += (sender, args) => { Settings.General.StartWithWindows = startWithWindowsBox.IsChecked == true; StartupRegistration.Apply(Settings.General.StartWithWindows); Settings.Save(); };
        startMinimisedBox.Click += (sender, args) => { Settings.General.StartMinimised = startMinimisedBox.IsChecked == true; Settings.Save(); };
        closeHidesBox.Click += (sender, args) => { Settings.General.CloseHidesToTray = closeHidesBox.IsChecked == true; Settings.Save(); };
    }

    // ---- Servers ----
    ServerProfile? SelectedProfile => profilesList.SelectedItem as ServerProfile;

    void WireServersTab()
    {
        if (Settings.Profiles.Count == 0) { Settings.Profiles.Add(new ServerProfile()); Settings.DefaultProfileId = Settings.Profiles[0].Id; Settings.Save(); }
        RefreshProfilesList(Settings.ActiveProfile);
        profilesList.SelectionChanged += (sender, args) => LoadProfileFields();
        addProfileButton.Click += (sender, args) => { var newProfile = new ServerProfile { Name = "New server" }; Settings.Profiles.Add(newProfile); Settings.Save(); RefreshProfilesList(newProfile); };
        removeProfileButton.Click += async (sender, args) =>
        {
            if (SelectedProfile == null || Settings.Profiles.Count == 1) return;
            var wasActive = SelectedProfile == Settings.ActiveProfile;
            Settings.Profiles.Remove(SelectedProfile);
            if (wasActive) Settings.DefaultProfileId = Settings.Profiles[0].Id;
            Settings.Save(); RefreshProfilesList(Settings.ActiveProfile);
            if (wasActive) await App.StartPlayerAsync(false);
        };
        defaultProfileButton.Click += async (sender, args) => { if (SelectedProfile == null) return; Settings.DefaultProfileId = SelectedProfile.Id; Settings.Save(); RefreshProfilesList(SelectedProfile); LoadDevices(); await App.StartPlayerAsync(false); };
        foreach (var profileBox in new[] { profileNameBox, profileHostBox, profilePortBox, profileUserBox }) profileBox.LostFocus += async (sender, args) => await SaveProfileFieldsAsync();
        profilePasswordBox.LostFocus += async (sender, args) => await SaveProfileFieldsAsync();
        testConnectionButton.Click += async (sender, args) => await TestConnectionAsync();
        findServersButton.Click += async (sender, args) => await FindServersAsync();
        discoveredList.SelectionChanged += (sender, args) => useServerButton.IsEnabled = discoveredList.SelectedItem != null;
        useServerButton.Click += async (sender, args) =>
        {
            if (discoveredList.SelectedItem is not DiscoveredServer server || SelectedProfile == null) return;
            profileHostBox.Text = server.Address; profilePortBox.Text = server.HttpPort.ToString();
            if (SelectedProfile.Host == "" && (SelectedProfile.Name is "Home" or "New server")) profileNameBox.Text = server.Name;
            await SaveProfileFieldsAsync();
        };
    }

    void RefreshProfilesList(ServerProfile? selectProfile)
    {
        loadingControls = true;
        profilesList.ItemsSource = null; profilesList.ItemsSource = Settings.Profiles;
        profilesList.SelectedItem = selectProfile;
        loadingControls = false;
        LoadProfileFields();
    }

    void LoadProfileFields()
    {
        var profile = SelectedProfile;
        loadingControls = true;
        profileNameBox.Text = profile?.Name ?? ""; profileHostBox.Text = profile?.Host ?? ""; profilePortBox.Text = profile?.Port.ToString() ?? "";
        profileUserBox.Text = profile?.Username ?? ""; profilePasswordBox.Password = profile?.Password ?? "";
        defaultProfileButton.IsEnabled = profile != null && profile != Settings.ActiveProfile;
        testResultText.Text = profile == Settings.ActiveProfile ? "This is the active profile." : "";
        loadingControls = false;
    }

    async Task SaveProfileFieldsAsync()
    {
        var profile = SelectedProfile;
        if (profile == null || loadingControls) return;
        var serverChanged = profile.Host != profileHostBox.Text.Trim() || profile.Port.ToString() != profilePortBox.Text.Trim();
        profile.Name = profileNameBox.Text.Trim() == "" ? "Server" : profileNameBox.Text.Trim();
        profile.Host = profileHostBox.Text.Trim();
        profile.Port = int.TryParse(profilePortBox.Text, out var port) ? port : 9000;
        profile.Username = profileUserBox.Text.Trim();
        if (profile.Password != profilePasswordBox.Password) profile.Password = profilePasswordBox.Password;
        Settings.Save();
        RefreshProfilesList(profile);
        if (serverChanged && profile == Settings.ActiveProfile) await App.StartPlayerAsync(false);
    }

    async Task TestConnectionAsync()
    {
        await SaveProfileFieldsAsync();
        if (SelectedProfile is not { Host.Length: > 0 } profile) { testResultText.Text = "Enter an address first."; return; }
        testResultText.Text = "Connecting…";
        try
        {
            using var lyrionClient = new LyrionClient(profile);
            var status = await lyrionClient.GetServerStatusAsync();
            testResultText.Text = $"Connected: Lyrion {status.Version}, {status.TotalSongs:N0} tracks, {status.TotalAlbums:N0} albums, {status.TotalArtists:N0} artists, {status.PlayerCount} players.";
        }
        catch (Exception connectError) { testResultText.Text = $"Could not connect: {connectError.Message}"; }
    }

    async Task FindServersAsync()
    {
        findServersButton.IsEnabled = false; findResultText.Text = "Searching…";
        try
        {
            var servers = await ServerFinder.FindAsync(TimeSpan.FromSeconds(2));
            discoveredList.ItemsSource = servers;
            findResultText.Text = servers.Count == 0 ? "No servers answered. For a VPN or another subnet, type the address above." : $"Found {servers.Count}. Select one and press Use selected.";
        }
        catch (Exception findError) { findResultText.Text = $"Search failed: {findError.Message}"; }
        findServersButton.IsEnabled = true;
    }

    // ---- Audio ----
    void WireAudioTab()
    {
        exclusiveBox.IsChecked = Settings.Audio.Exclusive;
        playerNameBox.Text = Settings.Audio.PlayerName;
        latencyBox.Text = Settings.Audio.LatencyMs.ToString();
        extraArgumentsBox.Text = Settings.Audio.ExtraArguments;
        deviceCombo.SelectionChanged += async (sender, args) => { if (!loadingControls && deviceCombo.SelectedItem is OutputDevice device) await App.SelectDeviceAsync(device); };
        fallbackCombo.SelectionChanged += (sender, args) =>
        {
            if (loadingControls || fallbackCombo.SelectedItem is not OutputDevice fallback) return;
            Settings.Audio.FallbackDeviceName = fallback == noFallback ? "" : fallback.Name; Settings.Audio.FallbackDeviceHostApi = fallback == noFallback ? "" : fallback.HostApi; Settings.Save();
        };
        refreshDevicesButton.Click += async (sender, args) => await App.RefreshDevicesAsync();
        exclusiveBox.Click += async (sender, args) => { Settings.Audio.Exclusive = exclusiveBox.IsChecked == true; Settings.Save(); await App.StartPlayerAsync(true); };
        playerNameBox.LostFocus += async (sender, args) => await SaveAudioTextAsync();
        latencyBox.LostFocus += async (sender, args) => await SaveAudioTextAsync();
        extraArgumentsBox.LostFocus += async (sender, args) => await SaveAudioTextAsync();
    }

    async Task SaveAudioTextAsync()
    {
        var newName = playerNameBox.Text.Trim() == "" ? Settings.Audio.PlayerName : playerNameBox.Text.Trim();
        var newLatency = int.TryParse(latencyBox.Text, out var latency) ? Math.Clamp(latency, 20, 2000) : Settings.Audio.LatencyMs;
        var newExtra = extraArgumentsBox.Text.Trim();
        latencyBox.Text = newLatency.ToString();
        if (newName == Settings.Audio.PlayerName && newLatency == Settings.Audio.LatencyMs && newExtra == Settings.Audio.ExtraArguments) return;
        Settings.Audio.PlayerName = newName; Settings.Audio.LatencyMs = newLatency; Settings.Audio.ExtraArguments = newExtra;
        Settings.Save();
        await App.StartPlayerAsync(true);
    }

    void LoadDevices()
    {
        loadingControls = true;
        var profile = Settings.ActiveProfile;
        var deviceView = new ListCollectionView(App.Devices);
        deviceView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OutputDevice.Driver)));
        deviceCombo.ItemsSource = deviceView;
        deviceCombo.SelectedItem = profile == null ? null : App.FindDevice(profile.DeviceName, profile.DeviceHostApi);
        var fallbackChoices = new List<OutputDevice> { noFallback };
        fallbackChoices.AddRange(App.Devices);
        fallbackCombo.ItemsSource = fallbackChoices;
        fallbackCombo.SelectedItem = App.FindDevice(Settings.Audio.FallbackDeviceName, Settings.Audio.FallbackDeviceHostApi) ?? noFallback;
        loadingControls = false;
    }

    void ShowPlayerStatus()
    {
        var player = App.Player;
        var detail = player.State == PlayerState.Running ? (player.ExclusiveActive ? " · WASAPI exclusive" : " · shared") + (player.OutputSampleRate > 0 ? $" · {player.OutputSampleRate / 1000.0:0.#} kHz" : "") : "";
        playerStatusText.Text = player.StatusText + detail + (App.UsingFallbackDevice ? " (fallback)" : "");
    }

    void WireAboutTab()
    {
        versionText.Text = $"WinSqueeze {typeof(App).Assembly.GetName().Version?.ToString(3)}";
        dataFolderText.Text = (AppPaths.IsPortable ? "Portable mode. " : "") + $"Settings and logs: {AppPaths.DataFolder}";
        squeezeliteSourceLink.RequestNavigate += (sender, args) => Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true });
        openLogsButton.Click += (sender, args) => Process.Start(new ProcessStartInfo(AppPaths.LogsFolder) { UseShellExecute = true });
    }
}
