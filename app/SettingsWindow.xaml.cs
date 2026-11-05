using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WinSqueeze.Audio;
using WinSqueeze.Server;
using WinSqueeze.Settings;
using WinSqueeze.Shell;
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
        WireGeneralTab(); WireServersTab(); WireAudioTab(); WireLibraryTab(); WireBrowsingTab(); WireAppearanceTab(); WireAboutTab();
        App.DevicesChanged += LoadDevices;
        App.StatusChanged += ShowPlayerStatus;
        Closed += (sender, args) => { App.DevicesChanged -= LoadDevices; App.StatusChanged -= ShowPlayerStatus; };
        LoadDevices(); ShowPlayerStatus();
    }

    public void SelectTab(string tabName)
    {
        var tabByName = new Dictionary<string, TabItem> { ["General"] = generalTab, ["Servers"] = serversTab, ["Audio"] = audioTab, ["Library"] = libraryTab, ["Browsing"] = browsingTab, ["About"] = aboutTab };
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
        exportSettingsButton.Click += (sender, args) =>
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog { FileName = $"WinSqueeze-settings-{DateTime.Now:yyyy-MM-dd}.json", Filter = "WinSqueeze settings (*.json)|*.json" };
            if (saveDialog.ShowDialog(this) != true) return;
            System.IO.File.WriteAllText(saveDialog.FileName, Settings.ExportJson());
            settingsFileText.Text = $"Exported to {saveDialog.FileName} (passwords left out).";
        };
        importSettingsButton.Click += async (sender, args) =>
        {
            var openDialog = new Microsoft.Win32.OpenFileDialog { Filter = "WinSqueeze settings (*.json)|*.json" };
            if (openDialog.ShowDialog(this) != true) return;
            try { await App.ReplaceSettingsAsync(AppSettings.Import(System.IO.File.ReadAllText(openDialog.FileName), Settings)); Close(); App.OpenSettings("General"); }
            catch (Exception importError) { settingsFileText.Text = $"Could not import: {importError.Message}"; }
        };
        otherSqueezeliteCombo.SelectedItem = otherSqueezeliteCombo.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == Settings.General.OtherSqueezelite.ToString());
        otherSqueezeliteCombo.SelectionChanged += (sender, args) => { if (otherSqueezeliteCombo.SelectedItem is ComboBoxItem chosen) { Settings.General.OtherSqueezelite = Enum.Parse<OtherSqueezeliteAction>((string)chosen.Tag); Settings.Save(); } };
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

    void WireLibraryTab()
    {
        var library = Settings.Library;
        folderAlbumsRadio.IsChecked = library.AlbumSource != "tags"; tagAlbumsRadio.IsChecked = library.AlbumSource == "tags";
        anyCreditRadio.IsChecked = library.ContributorGrouping != "albumartist"; albumArtistRadio.IsChecked = library.ContributorGrouping == "albumartist";
        separatorsBox.Text = library.NameSeparators; refreshMinutesBox.Text = library.RefreshMinutes.ToString();
        // Album source and grouping switch instantly: both are already in the index
        void SaveChoice() { library.AlbumSource = tagAlbumsRadio.IsChecked == true ? "tags" : "folders"; library.ContributorGrouping = albumArtistRadio.IsChecked == true ? "albumartist" : "any"; Settings.Save(); App.NotifyLibraryChanged(); }
        foreach (var radio in new[] { folderAlbumsRadio, tagAlbumsRadio, anyCreditRadio, albumArtistRadio }) radio.Click += (sender, args) => SaveChoice();
        hideOwnComposersBox.IsChecked = library.HideOwnComposers;
        hideOwnComposersBox.Click += (sender, args) => { library.HideOwnComposers = hideOwnComposersBox.IsChecked == true; Settings.Save(); App.NotifyLibraryChanged(); };
        separatorsBox.LostFocus += async (sender, args) => { if (separatorsBox.Text.Trim() == library.NameSeparators) return; library.NameSeparators = separatorsBox.Text.Trim(); Settings.Save(); await App.Library.RefreshAsync(true); };
        refreshMinutesBox.LostFocus += (sender, args) => { library.RefreshMinutes = int.TryParse(refreshMinutesBox.Text, out var minutes) ? Math.Clamp(minutes, 1, 1440) : library.RefreshMinutes; refreshMinutesBox.Text = library.RefreshMinutes.ToString(); Settings.Save(); };
        refreshLibraryButton.Click += async (sender, args) => { libraryStatusText.Text = "Checking the server…"; await App.Library.RefreshAsync(false); ShowLibraryStatus(); };
        rebuildLibraryButton.Click += async (sender, args) => { libraryStatusText.Text = "Rebuilding…"; await App.Library.RefreshAsync(true); ShowLibraryStatus(); };
        clearArtButton.Click += (sender, args) => { try { App.Library.ClearArtCache(); } catch { } ShowLibraryStatus(); };
        App.Library.Changed += ShowLibraryStatus;
        Closed += (sender, args) => App.Library.Changed -= ShowLibraryStatus;
        ShowLibraryStatus();
    }

    void ShowLibraryStatus()
    {
        if (App.Library.ProgressText != "") { libraryStatusText.Text = App.Library.ProgressTotal > 0 ? $"{App.Library.ProgressText}: {App.Library.ProgressDone:N0} of {App.Library.ProgressTotal:N0}" : App.Library.ProgressText; return; }
        try { libraryStatusText.Text = $"Cache size {App.Library.CacheBytes() / 1048576.0:0.#} MB. Stored in {AppPaths.DataFolder}."; } catch { libraryStatusText.Text = ""; }
    }

    static readonly (string value, string label)[] groupingNames = { ("none", "None"), ("artist", "Album artist"), ("year", "Year"), ("decade", "Decade"), ("added", "Date added"), ("format", "Format"), ("initial", "First letter") };
    static readonly (string area, string label)[] browseAreas = { ("genre", "Genres"), ("artist", "Artists"), ("composer", "Composers"), ("performer", "Performers"), ("albums", "Albums") };

    void WireBrowsingTab()
    {
        var library = Settings.Library;
        var areaBoxes = new List<(string area, ComboBox box)>();
        var filling = false; // refilling the menus fires SelectionChanged; only a user's pick is saved
        void SaveAndPush() { Settings.Save(); App.NotifyLibraryChanged(); }
        // Each area's menu lists None plus the offered groupings
        void FillAreaBoxes()
        {
            filling = true;
            foreach (var (area, box) in areaBoxes)
            {
                var current = library.DefaultGrouping.TryGetValue(area, out var chosen) ? chosen : "none";
                box.ItemsSource = groupingNames.Where(name => name.value == "none" || library.BrowseGroupings.Contains(name.value)).Select(name => new ComboBoxItem { Content = name.label, Tag = name.value }).ToList();
                box.SelectedItem = ((List<ComboBoxItem>)box.ItemsSource).FirstOrDefault(item => (string)item.Tag == current) ?? ((List<ComboBoxItem>)box.ItemsSource)[0];
            }
            filling = false;
        }
        foreach (var (value, label) in groupingNames.Skip(1))
        {
            var choiceBox = new CheckBox { Content = label, IsChecked = library.BrowseGroupings.Contains(value), Margin = new Thickness(0, 4, 16, 0) };
            choiceBox.Click += (sender, args) => { library.BrowseGroupings.Remove(value); if (choiceBox.IsChecked == true) library.BrowseGroupings.Add(value); FillAreaBoxes(); SaveAndPush(); };
            groupingChoicesPanel.Children.Add(choiceBox);
        }
        foreach (var (area, label) in browseAreas)
        {
            defaultGroupingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = defaultGroupingGrid.RowDefinitions.Count - 1;
            var areaLabel = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            var areaBox = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
            Grid.SetRow(areaLabel, row); Grid.SetRow(areaBox, row); Grid.SetColumn(areaBox, 1);
            defaultGroupingGrid.Children.Add(areaLabel); defaultGroupingGrid.Children.Add(areaBox);
            areaBoxes.Add((area, areaBox));
        }
        FillAreaBoxes();
        foreach (var (area, box) in areaBoxes) box.SelectionChanged += (sender, args) => { if (!filling && box.SelectedItem is ComboBoxItem { Tag: string value }) { library.DefaultGrouping[area] = value; SaveAndPush(); } };
    }

    void WireAppearanceTab()
    {
        var general = Settings.General;
        var skinNames = new[] { ("auto", "Follow Windows (light or dark)"), ("dark", "Frosted dark"), ("light", "Frosted light"), ("classic", "Squeezebox classic"), ("amber", "Hi-fi amber") };
        skinBox.ItemsSource = skinNames.Select(skin => new ComboBoxItem { Content = skin.Item2, Tag = skin.Item1 }).ToList();
        skinBox.SelectedItem = ((List<ComboBoxItem>)skinBox.ItemsSource).FirstOrDefault(item => (string)item.Tag == general.Skin) ?? ((List<ComboBoxItem>)skinBox.ItemsSource)[0];
        skinBox.SelectionChanged += (sender, args) => { if (skinBox.SelectedItem is ComboBoxItem { Tag: string skin }) { general.Skin = skin; Settings.Save(); App.ApplySkin(); } };
        glassBox.IsChecked = general.Glass; framelessBox.IsChecked = general.Frameless;
        glassBox.Click += (sender, args) => { general.Glass = glassBox.IsChecked == true; Settings.Save(); App.ApplyWindowStyle(); };
        framelessBox.Click += (sender, args) => { general.Frameless = framelessBox.IsChecked == true; Settings.Save(); App.ApplyWindowStyle(); };
    }

    void WireAboutTab()
    {
        versionText.Text = $"WinSqueeze {App.Updater.CurrentVersion}";
        dataFolderText.Text = (AppPaths.IsPortable ? "Portable mode. " : "") + $"Settings and logs: {AppPaths.DataFolder}";
        squeezeliteSourceLink.RequestNavigate += (sender, args) => OpenUrl(args.Uri.AbsoluteUri);
        openLogsButton.Click += (sender, args) => Process.Start(new ProcessStartInfo(AppPaths.LogsFolder) { UseShellExecute = true });
        channelCombo.SelectedItem = channelCombo.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == Settings.General.UpdateChannel);
        channelCombo.SelectionChanged += async (sender, args) =>
        {
            if (channelCombo.SelectedItem is not ComboBoxItem chosen) return;
            Settings.General.UpdateChannel = (string)chosen.Tag; Settings.Save();
            await LoadVersionsAsync(); await App.Updater.CheckNowAsync();
        };
        autoUpdateBox.IsChecked = Settings.General.AutoUpdate;
        autoUpdateBox.Click += (sender, args) => { Settings.General.AutoUpdate = autoUpdateBox.IsChecked == true; Settings.Save(); };
        checkNowButton.Click += async (sender, args) => await App.Updater.CheckNowAsync();
        restartToUpdateButton.Click += async (sender, args) => await App.Updater.RestartToUpdateAsync();
        refreshVersionsButton.Click += async (sender, args) => await LoadVersionsAsync();
        versionsList.SelectionChanged += (sender, args) => ShowSelectedVersion();
        switchVersionButton.Click += async (sender, args) => { if (versionsList.SelectedItem is VersionItem item) await App.Updater.SwitchToAsync(item.Release); };
        versionDiscussionLink.Click += (sender, args) => { if (versionsList.SelectedItem is VersionItem item) OpenUrl(item.Release.DiscussionUrl == "" ? item.Release.Url : item.Release.DiscussionUrl); };
        App.Updater.Changed += ShowUpdateStatus;
        Closed += (sender, args) => App.Updater.Changed -= ShowUpdateStatus;
        ShowUpdateStatus();
        _ = LoadVersionsAsync();
        if (App.Settings.General.AutoUpdate) _ = App.Updater.CheckNowAsync(); // fetch anything released since the last 15-minute check
    }

    record VersionItem(ReleaseEntry Release, bool IsCurrent)
    {
        public string Label => $"{Release.Version}{(Release.IsDev ? "  dev" : "")}  ·  {Release.Published:d MMM yyyy HH:mm}";
        public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;
        public override string ToString() => Label + (IsCurrent ? "  ·  running now" : "");
    }

    async Task LoadVersionsAsync()
    {
        try
        {
            var releases = await App.Updater.ListReleasesAsync(Settings.General.UpdateChannel == "dev");
            versionsList.ItemsSource = releases.Select(release => new VersionItem(release, release.Version == App.Updater.CurrentVersion)).ToList();
            if (releases.Count == 0) versionNotesText.Text = "No versions published yet.";
        }
        catch (Exception listError) { versionNotesText.Text = $"Could not load versions: {listError.Message}"; }
    }

    void ShowSelectedVersion()
    {
        var selected = versionsList.SelectedItem as VersionItem;
        switchVersionButton.IsEnabled = selected != null && !selected.IsCurrent && App.Updater.IsInstalled;
        versionNotesText.Text = selected == null ? "" : (selected.Release.Notes.Length > 600 ? selected.Release.Notes[..600] + "…" : selected.Release.Notes);
    }

    void ShowUpdateStatus()
    {
        updateStatusText.Text = App.Updater.StatusText;
        restartToUpdateButton.Visibility = App.Updater.ReadyVersion == "" ? Visibility.Collapsed : Visibility.Visible;
        restartToUpdateButton.Content = $"Restart to update to {App.Updater.ReadyVersion}";
    }

    static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
