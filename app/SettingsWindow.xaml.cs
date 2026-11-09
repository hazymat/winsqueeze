using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
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
        InitializeComponent(); settingsTabs.SelectedItem = generalTab; // the first sidebar item is a section heading
        WireGeneralTab(); WireServersTab(); WireServerLibraryTab(); WireAudioTab(); WirePlaybackTab(); WireNowPlayingViewsTab(); WireLibraryTab(); WireBrowsingTab(); WireAppearanceTab(); WireMetersTab(); WireShortcutsAndNotifications(); WireRecordingTab(); WireAboutTab();
        App.DevicesChanged += LoadDevices;
        App.StatusChanged += ShowPlayerStatus;
        Closed += (sender, args) => { App.DevicesChanged -= LoadDevices; App.StatusChanged -= ShowPlayerStatus; };
        LoadDevices(); ShowPlayerStatus();
    }

    public void SelectTab(string tabName)
    {
        var tabByName = new Dictionary<string, TabItem> { ["General"] = generalTab, ["Servers"] = serversTab, ["ServerLibrary"] = serverLibraryTab, ["Audio"] = audioTab, ["Playback"] = playbackTab, ["NowPlayingViews"] = nowPlayingViewsTab, ["Library"] = libraryTab, ["Browsing"] = browsingTab, ["About"] = aboutTab };
        settingsTabs.SelectedItem = tabByName.TryGetValue(tabName, out var selectedTab) ? selectedTab : generalTab; // the first item is a section heading
    }

    // ---- Now playing views (SPEC.md › Now playing views): which tabs sit beside Up next, in what order, and where each looks ----
    static readonly Dictionary<string, string> infoNames = new() { ["artist"] = "Artist", ["album"] = "Album", ["lyrics"] = "Lyrics", ["plugin"] = "Server plugin", ["wikipedia"] = "Wikipedia", ["lastfm"] = "Last.fm", ["tags"] = "File's own tags", ["lrclib"] = "LRCLIB" };
    static readonly Brush quietBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), okBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x7A, 0x3A)), lineBrush = new SolidColorBrush(Color.FromRgb(0xEC, 0xEE, 0xF1)), lockedBrush = new SolidColorBrush(Color.FromRgb(0xF6, 0xF7, 0xF9));
    bool? infoPluginInstalled;

    void WireNowPlayingViewsTab()
    {
        var trackInfo = Settings.TrackInfo;
        lastFmKeyBox.Text = trackInfo.LastFmKey;
        timelineBox.IsChecked = trackInfo.Timeline; timelineBox.Click += (sender, args) => { trackInfo.Timeline = timelineBox.IsChecked == true; SaveTrackInfo(); };
        lastFmLink.RequestNavigate += (sender, args) => { Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true }); args.Handled = true; };
        lastFmKeyBox.LostFocus += (sender, args) =>
        {
            var hadKey = trackInfo.LastFmKey != ""; if (lastFmKeyBox.Text.Trim() == trackInfo.LastFmKey) return;
            trackInfo.LastFmKey = lastFmKeyBox.Text;
            if (!hadKey && trackInfo.LastFmKey != "") foreach (var sources in trackInfo.Sources.Values) foreach (var source in sources.Where(source => source.Key == "lastfm")) source.On = true; // a new key switches Last.fm on
            SaveTrackInfo(); DrawInfoSources();
        };
        settingsTabs.SelectionChanged += async (sender, args) => { if (args.Source != settingsTabs || settingsTabs.SelectedItem != nowPlayingViewsTab) return; infoPluginInstalled = await App.TrackInfo.PluginInstalledAsync(); DrawInfoSources(); };
        DrawInfoTabs(); DrawInfoSources();
    }

    void SaveTrackInfo() { Settings.Save(); App.NotifyTrackInfoChanged(); }

    void DrawInfoTabs()
    {
        infoTabsList.Children.Clear();
        infoTabsList.Children.Add(InfoRow(null, new InfoChoice { Key = "upnext" }, "Up next", "", null, DrawInfoTabs, arrows: true)); // always there, always first
        foreach (var tab in Settings.TrackInfo.Tabs) infoTabsList.Children.Add(InfoRow(Settings.TrackInfo.Tabs, tab, infoNames[tab.Key], tab.Key == "artist" ? "biography and photo" : tab.Key == "album" ? "review and background" : "", null, DrawInfoTabs, arrows: true));
    }

    void DrawInfoSources()
    {
        infoSourcesGrid.Children.Clear();
        var hasLastFmKey = Settings.TrackInfo.LastFmKey != "";
        foreach (var tabKey in TrackInfoSettings.TabKeys)
        {
            var column = new StackPanel();
            column.Children.Add(new Border { Background = lockedBrush, BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xE5, 0xE8)), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 6, 10, 6), Child = new TextBlock { Text = infoNames[tabKey], FontWeight = FontWeights.SemiBold } });
            var sources = Settings.TrackInfo.Sources[tabKey];
            foreach (var source in sources)
            {
                var (note, noteBrush) = source.Key switch { "plugin" => (infoPluginInstalled == true ? "✓ installed" : infoPluginInstalled == false ? "not installed" : "", infoPluginInstalled == true ? okBrush : quietBrush), "wikipedia" => ("via MusicBrainz", quietBrush), "lrclib" => ("online", quietBrush), "lastfm" => (hasLastFmKey ? "" : "needs a key", quietBrush), _ => ("", quietBrush) };
                column.Children.Add(InfoRow(sources, source, infoNames[source.Key], note, noteBrush, DrawInfoSources, arrows: false, usable: source.Key != "lastfm" || hasLastFmKey));
            }
            infoSourcesGrid.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xE5, 0xE8)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true, Margin = new Thickness(tabKey == "artist" ? 0 : 4, 0, tabKey == "lyrics" ? 0 : 4, 0), Background = Brushes.White, Child = column });
        }
    }

    // One row: drag grip (or lock), tickbox, name, note, and for tabs up/down arrows. list = null for the fixed Up next row
    FrameworkElement InfoRow(List<InfoChoice>? list, InfoChoice choice, string name, string note, Brush? noteBrush, Action redraw, bool arrows, bool usable = true)
    {
        var locked = list == null; var position = list?.IndexOf(choice) ?? -1;
        var row = new DockPanel { LastChildFill = true };
        var grip = new TextBlock { Text = locked ? "\uE72E" : "\uE76F", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = locked ? 12 : 14, Foreground = quietBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Width = 14, Cursor = locked ? null : System.Windows.Input.Cursors.SizeAll, ToolTip = locked ? null : "Drag to reorder" };
        if (!locked) { grip.LayoutTransform = new RotateTransform(90); grip.MouseLeftButtonDown += (sender, args) => DragDrop.DoDragDrop(grip, new DataObject(typeof(InfoChoice), choice), DragDropEffects.Move); }
        DockPanel.SetDock(grip, Dock.Left); row.Children.Add(grip);
        var box = new CheckBox { Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, IsChecked = locked || (usable && choice.On), IsEnabled = !locked && usable };
        box.Click += (sender, args) => { choice.On = box.IsChecked == true; SaveTrackInfo(); };
        DockPanel.SetDock(box, Dock.Left); row.Children.Add(box);
        var label = new TextBlock { Text = name, Margin = new Thickness(6, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = arrows ? FontWeights.SemiBold : FontWeights.Normal, Foreground = locked || !usable ? quietBrush : Brushes.Black, TextTrimming = TextTrimming.CharacterEllipsis };
        DockPanel.SetDock(label, Dock.Left); row.Children.Add(label);
        if (arrows)
        {
            var arrowPanel = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(arrowPanel, Dock.Right);
            foreach (var (glyph, step) in new[] { ("\uE70E", -1), ("\uE70D", 1) })
            {
                var arrow = new Button { Content = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(4, 0, 0, 0), IsEnabled = !locked && position + step >= 0 && position + step < list!.Count, ToolTip = step < 0 ? "Move up" : "Move down" };
                arrow.Click += (sender, args) => { list!.RemoveAt(position); list.Insert(position + step, choice); SaveTrackInfo(); redraw(); };
                arrowPanel.Children.Add(arrow);
            }
            row.Children.Add(arrowPanel);
        }
        var noteText = new TextBlock { Text = note, Foreground = noteBrush ?? quietBrush, FontSize = arrows ? 13 : 11.5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = arrows ? HorizontalAlignment.Left : HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis };
        row.Children.Add(noteText);
        var rowBorder = new Border { Child = row, Padding = new Thickness(10, 7, 10, 7), Background = locked ? lockedBrush : Brushes.White, BorderBrush = lineBrush, BorderThickness = new Thickness(0, locked || position == 0 && !arrows ? 0 : 1, 0, 0), AllowDrop = !locked };
        if (!locked)
        {
            rowBorder.DragOver += (sender, args) => { args.Effects = args.Data.GetData(typeof(InfoChoice)) is InfoChoice dragged && list!.Contains(dragged) ? DragDropEffects.Move : DragDropEffects.None; args.Handled = true; };
            rowBorder.Drop += (sender, args) => { if (args.Data.GetData(typeof(InfoChoice)) is not InfoChoice dragged || !list!.Contains(dragged) || dragged == choice) return; list.Remove(dragged); list.Insert(Math.Min(position, list.Count), dragged); SaveTrackInfo(); redraw(); }; // dropped on a row: takes that row's place
        }
        return rowBorder;
    }

    void WireGeneralTab()
    {
        startWithWindowsBox.IsChecked = Settings.General.StartWithWindows;
        startMinimisedBox.IsChecked = Settings.General.StartMinimised;
        closeHidesBox.IsChecked = Settings.General.CloseToTray;
        resumeOnStartupBox.IsChecked = Settings.General.ResumeOnStartup;
        // Both ticked = music can start on boot: show the bracket and warning
        void ShowAutoPlayWarning() { var autoPlayVisibility = Settings.General.StartWithWindows && Settings.General.ResumeOnStartup ? Visibility.Visible : Visibility.Collapsed; autoPlayBracket.Visibility = autoPlayVisibility; autoPlayWarning.Visibility = autoPlayVisibility; }
        ShowAutoPlayWarning();
        startWithWindowsBox.Click += (sender, args) => { Settings.General.StartWithWindows = startWithWindowsBox.IsChecked == true; StartupRegistration.Apply(Settings.General.StartWithWindows); Settings.Save(); ShowAutoPlayWarning(); };
        startMinimisedBox.Click += (sender, args) => { Settings.General.StartMinimised = startMinimisedBox.IsChecked == true; Settings.Save(); };
        resumeOnStartupBox.Click += (sender, args) => { Settings.General.ResumeOnStartup = resumeOnStartupBox.IsChecked == true; Settings.Save(); ShowAutoPlayWarning(); };
        showHintsBox.IsChecked = !Settings.General.HideHints;
        showHintsBox.Click += (sender, args) => { Settings.General.HideHints = showHintsBox.IsChecked != true; Settings.Save(); App.NotifyHintsChanged(); };
        resetHintsButton.Click += (sender, args) => { Settings.General.DismissedHints.Clear(); Settings.General.HideHints = false; showHintsBox.IsChecked = true; Settings.Save(); App.NotifyHintsChanged(); };
        closeHidesBox.Click += (sender, args) => { Settings.General.CloseToTray = closeHidesBox.IsChecked == true; Settings.Save(); };
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

    // ---- Playback: the selected player's Lyrion prefs, read when the tab opens, written only when the user changes one ----
    string playbackPlayerId = "";

    void WirePlaybackTab()
    {
        settingsTabs.SelectionChanged += async (sender, args) => { if (args.Source == settingsTabs && settingsTabs.SelectedItem == playbackTab) await LoadPlaybackAsync(); };
        transitionTypeCombo.SelectionChanged += async (sender, args) => { if (transitionTypeCombo.SelectedItem is ComboBoxItem chosen) await SetPlayerPrefAsync("transitionType", (string)chosen.Tag); ShowTransitionControls(); };
        transitionDurationSlider.ValueChanged += (sender, args) => transitionDurationText.Text = $"{transitionDurationSlider.Value:0} s";
        transitionDurationSlider.PreviewMouseUp += async (sender, args) => await SetPlayerPrefAsync("transitionDuration", $"{transitionDurationSlider.Value:0}");
        transitionDurationSlider.PreviewKeyUp += async (sender, args) => await SetPlayerPrefAsync("transitionDuration", $"{transitionDurationSlider.Value:0}");
        transitionSmartBox.Click += async (sender, args) => await SetPlayerPrefAsync("transitionSmart", transitionSmartBox.IsChecked == true ? "1" : "0");
        replayGainCombo.SelectionChanged += async (sender, args) => { if (replayGainCombo.SelectedItem is ComboBoxItem chosen) await SetPlayerPrefAsync("replayGainMode", (string)chosen.Tag); };
    }

    async Task LoadPlaybackAsync()
    {
        playbackPlayerId = App.Session.PlayerId;
        var playerName = playbackPlayerId == Settings.PlayerMac ? $"{Settings.Audio.PlayerName} (this PC)" : playbackPlayerId;
        try
        {
            using var playersDocument = System.Text.Json.JsonDocument.Parse(App.Session.LastPlayersJson);
            foreach (var player in playersDocument.RootElement.EnumerateArray()) if (LyrionClient.ReadString(player, "playerid") == playbackPlayerId && playbackPlayerId != Settings.PlayerMac) playerName = LyrionClient.ReadString(player, "name");
        }
        catch (System.Text.Json.JsonException) { }
        playbackPlayerText.Text = $"Settings for {playerName}";
        var available = !App.IsOffline;
        foreach (var control in new Control[] { transitionTypeCombo, transitionDurationSlider, transitionSmartBox, replayGainCombo }) control.IsEnabled = available;
        playbackStatusText.Text = available ? "" : "Not available while playing offline.";
        if (!available) return;
        try
        {
            async Task<string> ReadPref(string name) => LyrionClient.ReadString(await App.ServerRequestAsync(playbackPlayerId, "playerpref", name, "?"), "_p2");
            var transitionType = await ReadPref("transitionType"); var transitionDuration = await ReadPref("transitionDuration"); var transitionSmart = await ReadPref("transitionSmart"); var replayGainMode = await ReadPref("replayGainMode");
            loadingControls = true;
            transitionTypeCombo.SelectedItem = transitionTypeCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == (transitionType == "" ? "0" : transitionType));
            transitionDurationSlider.Value = double.TryParse(transitionDuration, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
            transitionDurationText.Text = $"{transitionDurationSlider.Value:0} s";
            transitionSmartBox.IsChecked = transitionSmart != "0";
            replayGainCombo.SelectedItem = replayGainCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == (replayGainMode == "" ? "0" : replayGainMode));
            loadingControls = false;
            ShowTransitionControls();
        }
        catch (Exception readError) { loadingControls = false; playbackStatusText.Text = $"Could not read the player's settings: {readError.Message}"; }
    }

    void ShowTransitionControls() { var transitionOn = transitionTypeCombo.SelectedItem is ComboBoxItem { Tag: string tag } && tag != "0"; transitionDurationSlider.IsEnabled = transitionOn && !App.IsOffline; transitionSmartBox.IsEnabled = transitionOn && !App.IsOffline; }

    async Task SetPlayerPrefAsync(string prefName, string value)
    {
        if (loadingControls || playbackPlayerId == "" || App.IsOffline) return;
        try { await App.ServerRequestAsync(playbackPlayerId, "playerpref", prefName, value); playbackStatusText.Text = ""; if (playbackPlayerId == Settings.PlayerMac) await App.LoadOwnPlayerPrefsAsync(); }
        catch (Exception writeError) { playbackStatusText.Text = $"Could not change it: {writeError.Message}"; }
    }

    // ---- Server › Library: start Lyrion scans and follow them (SPEC.md › Server library) ----
    readonly System.Windows.Threading.DispatcherTimer scanTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    // What the last scan this window watched added, kept for the session and shown while the server's last scan time still matches
    static (long lastScan, string title, string albums, bool any)? lastScanReport;
    static long scanBaseline; // the server's last scan time before the current scan: albums added after it are new
    bool scanWasRunning, scanStepsSeen, scanPollBusy; DateTime? scanStartedAt; // when this page started the scan, or worked out from the server's own clock
    // Lyrion's internal step names (rescanprogress), in plain words; unknown ones are tidied up
    static readonly (string prefix, string label)[] scanStepLabels = { ("discovering_playlist", "Finding playlists"), ("discovering", "Finding files"), ("directory_deleted", "Removing deleted files"), ("directory_new", "Reading new files"), ("directory_changed", "Reading changed files"), ("directory", "Reading files"), ("playlist_deleted", "Removing deleted playlists"), ("playlist", "Reading playlists"), ("cleanup", "Tidying up"), ("mergeva", "Merging Various Artists albums"), ("dboptimize", "Optimising the database"), ("precacheartwork", "Preparing cover art"), ("updatestandaloneartwork", "Cover art"), ("artwork", "Cover art"), ("fulltext", "Building the search index"), ("contributor_picture", "Artist pictures"), ("wipedb", "Clearing the library"), ("onlinelibrary", "Online library"), ("persistent", "Ratings and play counts") };

    void WireServerLibraryTab()
    {
        scanTimer.Tick += async (sender, args) => await PollScanAsync();
        settingsTabs.SelectionChanged += async (sender, args) => { if (args.Source != settingsTabs) return; if (settingsTabs.SelectedItem == serverLibraryTab) { scanTimer.Start(); await PollScanAsync(); } else scanTimer.Stop(); };
        Closed += (sender, args) => scanTimer.Stop();
        rescanButton.Click += async (sender, args) => await SendScanCommandAsync("rescan");
        rescanPlaylistsButton.Click += async (sender, args) => await SendScanCommandAsync("rescan", "playlists");
        wipeCacheButton.Click += async (sender, args) =>
        {
            if (MessageBox.Show(this, "The server empties its library and reads every file again. Until it finishes, every player sees only part of the library.\n\nClear and rescan everything?", "Clear and rescan", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK) await SendScanCommandAsync("wipecache");
        };
        stopScanButton.Click += async (sender, args) => await SendScanCommandAsync("abortscan");
        showNewMusicButton.Click += (sender, args) => App.OpenSection("new");
    }

    // After a scan: bring the new library into WinSqueeze, then list the albums the server first saw during it
    async Task ReportNewAlbumsAsync(long addedAfter, long lastScan)
    {
        await App.Library.RefreshAsync(false);
        if (addedAfter <= 0) return;
        var queries = App.Library.Queries;
        var newAlbums = await Task.Run(() => queries.AlbumsAddedAfter(addedAfter));
        string Describe(Dictionary<string, object?> album) => $"{album["title"]}" + (album["artist"] is string artist && artist != "" ? $" · {artist}" : "");
        lastScanReport = (lastScan, newAlbums.Count == 0 ? "No new albums" : $"This scan added {newAlbums.Count} {(newAlbums.Count == 1 ? "album" : "albums")}", string.Join("\n", newAlbums.Take(6).Select(Describe)) + (newAlbums.Count > 6 ? $"\nand {newAlbums.Count - 6} more" : ""), newAlbums.Count > 0);
        ShowScanReport(lastScan);
    }

    void ShowScanReport(long lastScan)
    {
        var showing = lastScanReport is { } report && report.lastScan == lastScan;
        scanAddedCallout.Visibility = showing ? Visibility.Visible : Visibility.Collapsed;
        if (!showing) return;
        var current = lastScanReport!.Value;
        scanAddedTitle.Text = current.title; scanAddedList.Text = current.albums; scanAddedList.Visibility = current.any ? Visibility.Visible : Visibility.Collapsed; showNewMusicButton.Visibility = current.any ? Visibility.Visible : Visibility.Collapsed;
    }

    async Task SendScanCommandAsync(params object[] command)
    {
        try { await App.ServerRequestAsync("", command); scanActionText.Text = ""; if ((string)command[0] != "abortscan") scanStartedAt = DateTime.Now; }
        catch (Exception commandError) { scanActionText.Text = $"The server didn't take it: {commandError.Message}"; }
        await PollScanAsync();
    }

    async Task PollScanAsync()
    {
        if (scanPollBusy) return;
        scanPollBusy = true;
        try
        {
            if (App.IsOffline) { ShowScanIdle("Not available while playing offline", "", false); return; }
            var status = await App.ServerRequestAsync("", "serverstatus", 0, 0);
            if (LyrionClient.ReadString(status, "rescan") != "1")
            {
                var lastScan = LyrionClient.ReadLong(status, "lastscan");
                if (scanWasRunning)
                {
                    var addedAfter = scanBaseline > 0 ? scanBaseline : scanStartedAt is { } began ? new DateTimeOffset(began).ToUnixTimeSeconds() - 5 : 0; // the page opened mid-scan: when it started, by this PC's clock
                    scanWasRunning = false; scanStepsSeen = false; scanStartedAt = null;
                    _ = ReportNewAlbumsAsync(addedAfter, lastScan);
                }
                scanBaseline = lastScan;
                if (scanStartedAt is { } sentAt && DateTime.Now - sentAt > TimeSpan.FromSeconds(30)) scanStartedAt = null; // a scan asked for that never showed up
                var totals = $"{LyrionClient.ReadInt(status, "info total songs"):N0} tracks · {LyrionClient.ReadInt(status, "info total albums"):N0} albums · {LyrionClient.ReadInt(status, "info total artists"):N0} artists";
                var lastScanText = lastScan > 0 ? $"Last library scan finished {DescribeWhen(DateTimeOffset.FromUnixTimeSeconds(lastScan).LocalDateTime)}" : "No scan recorded yet";
                ShowScanIdle(lastScanText, totals, true); ShowScanReport(lastScan);
                var failure = LyrionClient.ReadString(status, "lastscanfailed");
                scanFailedText.Text = $"The last scan didn't finish: {failure}"; scanFailedCallout.Visibility = failure == "" ? Visibility.Collapsed : Visibility.Visible;
                return;
            }
            var progress = await App.ServerRequestAsync("", "rescanprogress");
            var stepNames = LyrionClient.ReadString(progress, "steps").Split(',', StringSplitOptions.RemoveEmptyEntries);
            // For its first seconds a new scan still reports the previous one's steps, all finished: show it as starting
            var starting = !scanStepsSeen && stepNames.All(stepName => LyrionClient.ReadInt(progress, stepName) >= 100);
            scanWasRunning = true; if (!starting) scanStepsSeen = true;
            if (scanStartedAt == null && !starting) scanStartedAt = DateTime.Now - ParseClock(LyrionClient.ReadString(progress, "totaltime")); // started elsewhere: count from the server's own elapsed time
            if (starting) stepNames = Array.Empty<string>();
            var activeStep = stepNames.FirstOrDefault(stepName => LyrionClient.ReadInt(progress, stepName) < 100) ?? stepNames.LastOrDefault() ?? "";
            var serverStepName = LyrionClient.ReadString(status, "progressname");
            var stepDone = LyrionClient.ReadLong(status, "progressdone"); var stepTotal = LyrionClient.ReadLong(status, "progresstotal");
            scanGlyph.Text = "\uE895"; scanGlyph.Foreground = new SolidColorBrush(Color.FromRgb(0x2F, 0x7F, 0xD8)); // sync arrows
            scanHeadlineText.Text = starting ? "Starting the scan" : serverStepName.Any(char.IsLower) ? serverStepName : activeStep == "" ? "Scanning" : ScanStepLabel(activeStep); // the server's own wording, unless it only has a code for this step
            var showCount = stepTotal > 0 && !starting;
            scanDetailText.Text = (showCount ? $"{stepDone:N0} of {stepTotal:N0}" : "") + (showCount && scanStartedAt != null ? "   ·   " : "") + (scanStartedAt is { } startedAt ? $"{FormatElapsed(DateTime.Now - startedAt)} so far" : "");
            scanBar.IsIndeterminate = !showCount; if (showCount) scanBar.Value = Math.Min(100, 100.0 * stepDone / stepTotal);
            scanInfoText.Text = starting ? "" : LyrionClient.ReadString(progress, "info");
            scanStepsPanel.Children.Clear();
            foreach (var stepName in stepNames)
            {
                var finished = LyrionClient.ReadInt(progress, stepName) >= 100;
                var stepRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                stepRow.Children.Add(new TextBlock { FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Width = 20, VerticalAlignment = VerticalAlignment.Center, Text = finished ? "\uE73E" : "\uE76C", Foreground = new SolidColorBrush(finished ? Color.FromRgb(0x2E, 0x7D, 0x32) : Color.FromRgb(0x2F, 0x7F, 0xD8)) });
                stepRow.Children.Add(new TextBlock { Text = ScanStepLabel(stepName), Foreground = new SolidColorBrush(finished ? Color.FromRgb(0x66, 0x66, 0x66) : Color.FromRgb(0x1B, 0x1B, 0x1B)), FontWeight = finished ? FontWeights.Normal : FontWeights.SemiBold });
                scanStepsPanel.Children.Add(stepRow);
            }
            scanRunningPanel.Visibility = Visibility.Visible; scanFailedCallout.Visibility = Visibility.Collapsed; scanAddedCallout.Visibility = Visibility.Collapsed; stopScanButton.Visibility = Visibility.Visible;
            foreach (var scanButton in new[] { rescanButton, rescanPlaylistsButton, wipeCacheButton }) scanButton.IsEnabled = false;
        }
        catch (Exception pollError) { ShowScanIdle("Can't reach the server", pollError.Message, false); }
        finally { scanPollBusy = false; }
    }

    void ShowScanIdle(string headline, string detail, bool serverReachable)
    {
        scanGlyph.Text = serverReachable ? "\uE73E" : "\uE7BA"; scanGlyph.Foreground = new SolidColorBrush(serverReachable ? Color.FromRgb(0x2E, 0x7D, 0x32) : Color.FromRgb(0xB2, 0x6B, 0x00));
        scanHeadlineText.Text = headline; scanDetailText.Text = detail;
        scanRunningPanel.Visibility = Visibility.Collapsed; stopScanButton.Visibility = Visibility.Collapsed; if (!serverReachable) { scanFailedCallout.Visibility = Visibility.Collapsed; scanAddedCallout.Visibility = Visibility.Collapsed; }
        foreach (var scanButton in new[] { rescanButton, rescanPlaylistsButton, wipeCacheButton }) scanButton.IsEnabled = serverReachable;
    }

    static string ScanStepLabel(string stepName)
    {
        var plainName = stepName.Split('/', '\\')[0].Trim('_', ' '); // some steps carry the music folder's path
        var compactName = plainName.Replace("_", "").Replace("-", "").ToLowerInvariant(); if (compactName.StartsWith("plugin")) compactName = compactName[6..];
        foreach (var (prefix, label) in scanStepLabels) if (compactName.StartsWith(prefix.Replace("_", ""))) return label;
        var spacedName = plainName.Replace('_', ' ').Trim();
        return spacedName == "" ? "Scanning" : char.ToUpper(spacedName[0]) + spacedName[1..].ToLowerInvariant();
    }

    static TimeSpan ParseClock(string clockText) => TimeSpan.TryParseExact(clockText, @"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : TimeSpan.Zero;
    static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}" : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
    static string DescribeWhen(DateTime when)
    {
        var ago = DateTime.Now - when;
        var agoText = ago.TotalMinutes < 1 ? "just now" : ago.TotalHours < 1 ? $"{(int)ago.TotalMinutes} min ago" : ago.TotalDays < 1 ? $"{(int)ago.TotalHours} h ago" : (int)ago.TotalDays == 1 ? "yesterday" : $"{(int)ago.TotalDays} days ago";
        return $"{when:ddd d MMM, HH:mm} ({agoText})";
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
        exclusiveBox.IsChecked = Settings.Audio.Exclusive; exclusiveWarningText.Visibility = Settings.Audio.Exclusive ? Visibility.Visible : Visibility.Collapsed;
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
        exclusiveBox.Click += async (sender, args) => { Settings.Audio.Exclusive = exclusiveBox.IsChecked == true; exclusiveWarningText.Visibility = Settings.Audio.Exclusive ? Visibility.Visible : Visibility.Collapsed; Settings.Save(); await App.StartPlayerAsync(true); };
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
        var detail = player.State == PlayerState.Running ? (player.ExclusiveActive ? " · WASAPI exclusive" : player.ExclusiveRefused ? " · shared (device refused exclusive)" : " · shared") + (player.OutputSampleRate > 0 ? $" · {player.OutputSampleRate / 1000.0:0.#} kHz" : "") : "";
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
        nameOrderBox.SelectedIndex = library.NameOrder == "forename" ? 1 : 0;
        readSurnameFirstBox.IsChecked = library.ReadSurnameFirst;
        readSurnameFirstBox.Click += async (sender, args) => { library.ReadSurnameFirst = readSurnameFirstBox.IsChecked == true; Settings.Save(); await App.Library.RefreshAsync(true); };
        nameOrderBox.SelectionChanged += async (sender, args) => { library.NameOrder = (string)((ComboBoxItem)nameOrderBox.SelectedItem).Tag; Settings.Save(); await App.Library.RefreshAsync(true); };
        hideOwnComposersBox.IsChecked = library.HideOwnComposers;
        hideOwnComposersBox.Click += (sender, args) => { library.HideOwnComposers = hideOwnComposersBox.IsChecked == true; Settings.Save(); App.NotifyLibraryChanged(); };
        var listCountChoices = new[] { ("group", "What the view groups by (e.g. album artists)"), ("tracks", "Tracks"), ("both", "Both") };
        foreach (var (value, label) in listCountChoices) listCountsBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        listCountsBox.SelectedIndex = Math.Max(0, Array.FindIndex(listCountChoices, choice => choice.Item1 == library.ListCounts));
        listCountsBox.SelectionChanged += (sender, args) => { library.ListCounts = (string)((ComboBoxItem)listCountsBox.SelectedItem).Tag; Settings.Save(); App.NotifyLibraryChanged(); };
        // Graphic for list sizes, with a live preview of three sample rows
        var listGraphicChoices = new[] { ("none", "None"), ("dots", "Dots"), ("bar", "Continuous bar"), ("bold", "Bolder and brighter") };
        foreach (var (value, label) in listGraphicChoices) listGraphicBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        listGraphicBox.SelectedIndex = Math.Max(0, Array.FindIndex(listGraphicChoices, choice => choice.Item1 == library.ListSizeGraphic));
        void DrawGraphicPreview()
        {
            listGraphicPreview.Children.Clear();
            var accent = new SolidColorBrush(Color.FromRgb(0x5A, 0xC8, 0xFF));
            foreach (var (name, count) in new[] { ("Jazz", 39), ("Folk", 4), ("Celtic", 1) })
            {
                var share = Math.Log(count + 1) / Math.Log(40);
                var row = new Grid { Height = 24 }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var nameText = new TextBlock { Text = name, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
                if (library.ListSizeGraphic == "bold") { nameText.FontWeight = FontWeight.FromOpenTypeWeight((int)(300 + share * 400)); nameText.Opacity = 0.45 + share * 0.55; }
                row.Children.Add(nameText);
                if (library.ListSizeGraphic == "dots")
                {
                    var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                    var lit = Math.Max(1, (int)Math.Round(share * 5));
                    for (var dot = 1; dot <= 5; dot++) dots.Children.Add(new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, Margin = new Thickness(0, 0, 3, 0), Fill = dot <= lit ? accent : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) });
                    Grid.SetColumn(dots, 1); row.Children.Add(dots);
                }
                if (library.ListSizeGraphic == "bar") { var bar = new Border { Height = 2, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Width = share * 280, Background = new LinearGradientBrush(Color.FromRgb(0x5A, 0xC8, 0xFF), Color.FromRgb(0xB4, 0x8C, 0xFF), 0), Opacity = 0.35 + share * 0.65, Margin = new Thickness(0, 0, 0, 2) }; Grid.SetColumnSpan(bar, 3); row.Children.Add(bar); }
                var countText = new TextBlock { Text = $"{count} album{(count == 1 ? "" : "s")}", Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0xAD)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(countText, 2); row.Children.Add(countText); listGraphicPreview.Children.Add(row);
            }
        }
        DrawGraphicPreview();
        listGraphicBox.SelectionChanged += (sender, args) => { library.ListSizeGraphic = (string)((ComboBoxItem)listGraphicBox.SelectedItem).Tag; Settings.Save(); App.NotifyLibraryChanged(); DrawGraphicPreview(); };
        separatorsBox.LostFocus += async (sender, args) => { if (separatorsBox.Text.Trim() == library.NameSeparators) return; library.NameSeparators = separatorsBox.Text.Trim(); Settings.Save(); await App.Library.RefreshAsync(true); };
        refreshMinutesBox.LostFocus += (sender, args) => { library.RefreshMinutes = int.TryParse(refreshMinutesBox.Text, out var minutes) ? Math.Clamp(minutes, 1, 1440) : library.RefreshMinutes; refreshMinutesBox.Text = library.RefreshMinutes.ToString(); Settings.Save(); };
        refreshLibraryButton.Click += async (sender, args) => { libraryStatusText.Text = "Checking the server…"; await App.Library.RefreshAsync(false); ShowLibraryStatus(); };
        rebuildLibraryButton.Click += async (sender, args) => { libraryStatusText.Text = "Rebuilding…"; await App.Library.RefreshAsync(true); ShowLibraryStatus(); };
        clearArtButton.Click += (sender, args) => { try { App.Library.ClearArtCache(); } catch { } ShowLibraryStatus(); };
        App.Library.Changed += ShowLibraryStatus;
        Closed += (sender, args) => App.Library.Changed -= ShowLibraryStatus;
        ShowLibraryStatus();
        // Library health lives in the main window (SPEC.md › Library health); this card says how many damaged files it lists
        void ShowDamagedCount() => damagedCountText.Text = App.Damaged.Files.Count == 0 ? "No damaged files found while playing." : $"{App.Damaged.Files.Count} damaged {(App.Damaged.Files.Count == 1 ? "file" : "files")} found while playing.";
        void OnDamagedChanged() => Dispatcher.BeginInvoke(ShowDamagedCount);
        App.Damaged.Changed += OnDamagedChanged;
        Closed += (sender, args) => App.Damaged.Changed -= OnDamagedChanged;
        openHealthButton.Click += (sender, args) => { App.OpenSection("health"); Close(); };
        ShowDamagedCount();
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
        customViewsButton.Click += (sender, args) => App.OpenSection("customViews");
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

    // Each row: keys ("Ctrl+F", alternatives split by " / ") shown as keycaps, then what they do
    void FillShortcutTable(Grid table, (string keys, string action)[] rows)
    {
        var keycapStyle = (Style)FindResource("Keycap"); var greyText = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        foreach (var (keys, action) in rows)
        {
            var rowIndex = table.RowDefinitions.Count; table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var keyPanel = new WrapPanel { Margin = new Thickness(0, 3, 12, 3) };
            var alternatives = keys.Split(" / ");
            for (var altIndex = 0; altIndex < alternatives.Length; altIndex++)
            {
                if (altIndex > 0) keyPanel.Children.Add(new TextBlock { Text = "or", Foreground = greyText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0) });
                foreach (var keyName in alternatives[altIndex].Split('+')) keyPanel.Children.Add(new Border { Style = keycapStyle, Child = new TextBlock { Text = keyName, FontFamily = new FontFamily("Segoe UI Semibold"), FontSize = 12 } });
            }
            var actionText = new TextBlock { Text = action, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetRow(keyPanel, rowIndex); Grid.SetRow(actionText, rowIndex); Grid.SetColumn(actionText, 1);
            if (rowIndex > 0) { var divider = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xEE, 0xEF, 0xF1)), BorderThickness = new Thickness(0, 1, 0, 0) }; Grid.SetRow(divider, rowIndex); Grid.SetColumnSpan(divider, 2); table.Children.Add(divider); }
            table.Children.Add(keyPanel); table.Children.Add(actionText);
        }
    }

    void WireShortcutsAndNotifications()
    {
        FillShortcutTable(windowShortcutsTable, new[] { ("Space", "Play / pause"), ("Ctrl+Left / Ctrl+Right", "Previous / next track"), ("Ctrl+Up / Ctrl+Down", "Volume up / down"), ("Ctrl+F", "Search (then ↓ into the results, arrows to move, Enter to play or open)"), ("Ctrl+N", "New playlist"), ("Ctrl+Z", "Undo (press again to keep going back)"), ("Ctrl+Y / Ctrl+Shift+Z", "Redo"), ("Ctrl+1 / Ctrl+2 / Ctrl+3", "Window mode"), ("Ctrl+,", "Settings"), ("Ctrl+Q", "Quit"), ("Esc", "Close one thing at a time (menu, selection, search), then go back") });
        FillShortcutTable(selectionShortcutsTable, new[] { ("Enter", "Play now"), ("N", "Play next"), ("E", "Add to end"), ("I", "Info"), ("Del", "Remove") });
        FillShortcutTable(globalShortcutsTable, new[] { ("Ctrl+Alt+Space", "Play / pause"), ("Ctrl+Alt+Left / Ctrl+Alt+Right", "Previous / next track"), ("Ctrl+Alt+Up / Ctrl+Alt+Down", "Volume up / down") });
        var general = Settings.General;
        globalHotkeysBox.IsChecked = general.GlobalHotkeys;
        globalHotkeysBox.Click += (sender, args) =>
        {
            general.GlobalHotkeys = globalHotkeysBox.IsChecked == true; Settings.Save();
            var taken = App.ApplyGlobalHotkeys();
            hotkeyStatusText.Text = taken.Count == 0 ? "" : $"Another app already uses Ctrl+Alt+{string.Join(", Ctrl+Alt+", taken)}.";
        };
        trackToastsBox.IsChecked = general.TrackToasts;
        trackToastsBox.Click += (sender, args) => { general.TrackToasts = trackToastsBox.IsChecked == true; Settings.Save(); };
    }

    void WireAppearanceTab()
    {
        var general = Settings.General;
        var skinNames = new[] { ("auto", "Follow Windows (light or dark)"), ("dark", "Frosted dark"), ("light", "Frosted light"), ("classic", "Squeezebox classic"), ("amber", "Hi-fi amber") };
        skinBox.ItemsSource = skinNames.Select(skin => new ComboBoxItem { Content = skin.Item2, Tag = skin.Item1 }).ToList();
        skinBox.SelectedItem = ((List<ComboBoxItem>)skinBox.ItemsSource).FirstOrDefault(item => (string)item.Tag == general.Skin) ?? ((List<ComboBoxItem>)skinBox.ItemsSource)[0];
        skinBox.SelectionChanged += (sender, args) => { if (skinBox.SelectedItem is ComboBoxItem { Tag: string skin }) { general.Skin = skin; Settings.Save(); App.ApplySkin(); } };
        // Accent: a chosen colour (or the skin's blue), and optionally the playing cover's on top
        void ShowAccent() => accentSwatch.Background = general.AccentColour == "" ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5c, 0xcf, 0xff)) : new System.Windows.Media.BrushConverter().ConvertFromString(general.AccentColour) as System.Windows.Media.Brush;
        ShowAccent();
        accentChooseButton.Click += (sender, args) =>
        {
            using var colourDialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
            if (general.AccentColour != "") colourDialog.Color = System.Drawing.ColorTranslator.FromHtml(general.AccentColour);
            if (colourDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            general.AccentColour = $"#{colourDialog.Color.R:x2}{colourDialog.Color.G:x2}{colourDialog.Color.B:x2}"; Settings.Save(); App.ApplySkin(); ShowAccent();
        };
        accentResetButton.Click += (sender, args) => { general.AccentColour = ""; Settings.Save(); App.ApplySkin(); ShowAccent(); };
        // Scale of the whole main window UI (replaces Ctrl+wheel zoom)
        uiScaleSlider.Value = Math.Round(general.UiScale * 100); uiScaleText.Text = $"{uiScaleSlider.Value}%";
        uiScaleSlider.ValueChanged += (sender, args) => { general.UiScale = uiScaleSlider.Value / 100; uiScaleText.Text = $"{uiScaleSlider.Value}%"; Settings.Save(); App.ApplyUiScale(); };
        uiScaleResetButton.Click += (sender, args) => uiScaleSlider.Value = 100;
        // Glass: how see-through the tint is, and its hue (0 = neutral); both preview live
        void ShowGlass()
        {
            glassTransparencyText.Text = general.GlassTransparency < 0 ? $"{(int)glassTransparencySlider.Value}% (default)" : $"{general.GlassTransparency}%";
            glassTintText.Text = general.GlassTintHue == 0 ? "Neutral" : $"{general.GlassTintHue}°";
            glassTintSwatch.Background = new System.Windows.Media.SolidColorBrush(general.GlassTintHue == 0 ? System.Windows.Media.Color.FromRgb(30, 32, 38) : HueColour(general.GlassTintHue));
        }
        bool fillingGlass = true;
        glassTransparencySlider.Value = general.GlassTransparency < 0 ? 62 : general.GlassTransparency; glassTintSlider.Value = general.GlassTintHue; fillingGlass = false; ShowGlass();
        glassTransparencySlider.ValueChanged += (sender, args) => { if (fillingGlass) return; general.GlassTransparency = (int)glassTransparencySlider.Value; Settings.Save(); App.ApplySkin(); ShowGlass(); };
        glassTintSlider.ValueChanged += (sender, args) => { if (fillingGlass) return; general.GlassTintHue = (int)glassTintSlider.Value; Settings.Save(); App.ApplySkin(); ShowGlass(); };
        glassResetButton.Click += (sender, args) => { general.GlassTransparency = -1; general.GlassTintHue = 0; fillingGlass = true; glassTransparencySlider.Value = 62; glassTintSlider.Value = 0; fillingGlass = false; Settings.Save(); App.ApplySkin(); ShowGlass(); };
        glassInactiveBox.IsChecked = general.GlassWhenInactive;
        glassInactiveBox.Click += (sender, args) => { general.GlassWhenInactive = glassInactiveBox.IsChecked == true; Settings.Save(); App.RefreshInactiveGlass(); };
        coverBackdropBox.IsChecked = general.CoverBackdrop;
        coverBackdropBox.Click += (sender, args) => { general.CoverBackdrop = coverBackdropBox.IsChecked == true; Settings.Save(); App.ApplySkin(); };
        coverAccentBox.IsChecked = general.CoverAccent;
        coverAccentBox.Click += (sender, args) => { general.CoverAccent = coverAccentBox.IsChecked == true; Settings.Save(); App.ApplySkin(); };
        glassBox.IsChecked = general.Glass; framelessBox.IsChecked = general.Frameless;
        glassBox.Click += (sender, args) => { general.Glass = glassBox.IsChecked == true; Settings.Save(); App.ApplyWindowStyle(); };
        framelessBox.Click += (sender, args) => { general.Frameless = framelessBox.IsChecked == true; Settings.Save(); App.ApplyWindowStyle(); };
    }

    // Meters: one slider row per setting, saved and pushed as it moves
    void WireMetersTab()
    {
        void AddRow(string label, string help, double minimum, double maximum, double step, Func<MeterSettings, double> read, Action<MeterSettings, double> write, string unit)
        {
            var valueText = new TextBlock { Width = 90, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider { Minimum = minimum, Maximum = maximum, TickFrequency = step, IsSnapToTickEnabled = true, Value = read(Settings.Meters), Width = 260, VerticalAlignment = VerticalAlignment.Center };
            void Show() => valueText.Text = $"{slider.Value:0.#} {unit}";
            Show();
            slider.ValueChanged += (sender, args) => { write(Settings.Meters, slider.Value); Show(); Settings.Save(); App.ApplyMeters(); };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = label, Width = 170, VerticalAlignment = VerticalAlignment.Center }); row.Children.Add(slider); row.Children.Add(valueText);
            metersPanel.Children.Add(row);
            metersPanel.Children.Add(new TextBlock { Text = help, Style = (Style)FindResource("Quiet"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        }
        void Build()
        {
            metersPanel.Children.Clear();
            var autoBox = new CheckBox { Content = "Follow each track's loudness: automatic 0 VU, and the spectrum lifts quiet recordings (so loud ones don't pin the meters and quiet ones still show)", IsChecked = Settings.Meters.AutoReference, Margin = new Thickness(0, 0, 0, 10) };
            autoBox.Click += (sender, args) => { Settings.Meters.AutoReference = autoBox.IsChecked == true; Settings.Save(); App.ApplyMeters(); };
            metersPanel.Children.Add(autoBox);
            void AddCheck(string label, Func<MeterSettings, bool> read, Action<MeterSettings, bool> write)
            {
                var checkBox = new CheckBox { Content = label, IsChecked = read(Settings.Meters), Margin = new Thickness(0, 0, 0, 10) };
                checkBox.Click += (sender, args) => { write(Settings.Meters, checkBox.IsChecked == true); Settings.Save(); App.ApplyMeters(); };
                metersPanel.Children.Add(checkBox);
            }
            AddCheck("Meters and analysers show the sound after the EQ", meters => meters.FollowEq, (meters, value) => meters.FollowEq = value);
            AddCheck("Meters and analysers drop with WinSqueeze's volume (off: they show the music at full level)", meters => meters.FollowVolume, (meters, value) => meters.FollowVolume = value);
            AddRow("0 VU reference", "When automatic is off: the RMS level that reads 0 on the VU meters. Higher (e.g. −14) makes the needles read lower; −18 is the usual digital alignment.", -24, -8, 1, meters => meters.ReferenceDbfs, (meters, value) => meters.ReferenceDbfs = value, "dBFS");
            AddRow("VU ballistics", "How quickly the needles follow the music. 300 ms is a classic VU.", 50, 1000, 10, meters => meters.VuIntegrationMs, (meters, value) => meters.VuIntegrationMs = value, "ms");
            AddRow("Spectrum fall", "How fast the spectrum columns fall back.", 50, 1000, 10, meters => meters.SpectrumFallMs, (meters, value) => meters.SpectrumFallMs = value, "ms");
        }
        Build();
        metersResetButton.Click += (sender, args) => { Settings.Meters = new MeterSettings(); Settings.Save(); App.ApplyMeters(); Build(); };
    }

    // A mid, fairly saturated colour for a hue, for the tint swatch
    static System.Windows.Media.Color HueColour(int hue)
    {
        double chroma = 0.45, section = hue / 60.0, second = chroma * (1 - Math.Abs(section % 2 - 1)), lightness = 0.3 - chroma / 2;
        var (red, green, blue) = section < 1 ? (chroma, second, 0.0) : section < 2 ? (second, chroma, 0.0) : section < 3 ? (0.0, chroma, second) : section < 4 ? (0.0, second, chroma) : section < 5 ? (second, 0.0, chroma) : (chroma, 0.0, second);
        return System.Windows.Media.Color.FromRgb((byte)((red + lightness) * 255), (byte)((green + lightness) * 255), (byte)((blue + lightness) * 255));
    }

    void WireRecordingTab()
    {
        var recording = Settings.Recording;
        recordButtonBox.IsChecked = recording.ShowButton; recordSplitBox.IsChecked = recording.SplitOnSongChange; recordFolderBox.Text = App.Recorder.Folder;
        recordButtonBox.Click += (sender, args) => { recording.ShowButton = recordButtonBox.IsChecked == true; Settings.Save(); App.RaiseStatusChanged(); };
        recordSplitBox.Click += (sender, args) => { recording.SplitOnSongChange = recordSplitBox.IsChecked == true; Settings.Save(); };
        recordFolderPerStationRadio.IsChecked = recording.FolderPerStation; recordFolderPerRecordingRadio.IsChecked = !recording.FolderPerStation;
        recordFolderPerStationRadio.Click += (sender, args) => { recording.FolderPerStation = true; Settings.Save(); };
        recordFolderPerRecordingRadio.Click += (sender, args) => { recording.FolderPerStation = false; Settings.Save(); };
        chooseRecordFolderButton.Click += (sender, args) =>
        {
            var folderDialog = new Microsoft.Win32.OpenFolderDialog { Title = "Save recordings in", InitialDirectory = System.IO.Directory.Exists(App.Recorder.Folder) ? App.Recorder.Folder : "" };
            if (folderDialog.ShowDialog(this) != true) return;
            recording.Folder = folderDialog.FolderName == Recorder.DefaultFolder ? "" : folderDialog.FolderName; Settings.Save(); recordFolderBox.Text = App.Recorder.Folder;
        };
        openRecordFolderButton.Click += (sender, args) => { System.IO.Directory.CreateDirectory(App.Recorder.Folder); Process.Start("explorer.exe", App.Recorder.Folder); };
    }

    // Everything a bug report needs, as plain text (SPEC.md › Diagnostics). No passwords
    string DiagnosticsReport()
    {
        static string LogTail(string fileName, int lineCount)
        {
            var path = System.IO.Path.Combine(AppPaths.LogsFolder, fileName);
            if (!System.IO.File.Exists(path)) return "(none)";
            using var reader = new System.IO.StreamReader(new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete)); // Squeezelite's log is open for writing
            var lines = reader.ReadToEnd().Split('\n'); return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - lineCount))).TrimEnd();
        }
        var player = App.Player; var audio = Settings.Audio; var profile = Settings.ActiveProfile;
        var report = new System.Text.StringBuilder();
        report.AppendLine($"WinSqueeze {App.Updater.CurrentVersion} · {Settings.General.UpdateChannel} channel{(AppPaths.IsPortable ? " · portable" : "")}");
        report.AppendLine($"Windows: {System.Runtime.InteropServices.RuntimeInformation.OSDescription} · {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture} · .NET {Environment.Version}");
        report.AppendLine($"Server: {(profile is { Host.Length: > 0 } ? $"{profile.Host}:{profile.Port}" : "none set")}");
        report.AppendLine($"Player: {audio.PlayerName} · {player.StatusText}{(player.State == PlayerState.Running ? (player.ExclusiveActive ? " · WASAPI exclusive" : player.ExclusiveRefused ? " · shared (device refused exclusive)" : " · shared") + (player.OutputSampleRate > 0 ? $" · {player.OutputSampleRate / 1000.0:0.#} kHz" : "") : "")}{(App.UsingFallbackDevice ? " (fallback device)" : "")}");
        report.AppendLine($"Output device: {(profile is null || profile.DeviceName == "" ? "Windows default" : $"{profile.DeviceName} ({profile.DeviceHostApi})")} · exclusive {(audio.Exclusive ? "on" : "off")}");
        report.AppendLine().AppendLine("--- crash.log (end) ---").AppendLine(LogTail("crash.log", 60));
        report.AppendLine().AppendLine("--- squeezelite.log (end) ---").AppendLine(LogTail("squeezelite.log", 60));
        return report.ToString();
    }

    void WireAboutTab()
    {
        versionText.Text = $"WinSqueeze {App.Updater.CurrentVersion}";
        dataFolderText.Text = (AppPaths.IsPortable ? "Portable mode. " : "") + $"Settings and logs: {AppPaths.DataFolder}";
        squeezeliteSourceLink.RequestNavigate += (sender, args) => OpenUrl(args.Uri.AbsoluteUri);
        openLogsButton.Click += (sender, args) => Process.Start(new ProcessStartInfo(AppPaths.LogsFolder) { UseShellExecute = true });
        copyDiagnosticsButton.Click += (sender, args) => { try { Clipboard.SetText(DiagnosticsReport()); copyDiagnosticsDone.Text = "Copied. Check it before posting anywhere public: it includes your server address."; } catch (Exception error) { copyDiagnosticsDone.Text = "Couldn't copy: " + error.Message; } };
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
