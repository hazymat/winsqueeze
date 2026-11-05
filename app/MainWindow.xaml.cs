using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using WinSqueeze.Library;
using WinSqueeze.Settings;
using WinSqueeze.Shell;
namespace WinSqueeze;

public partial class MainWindow : Window
{
    const string uiHost = "app.winsqueeze"; // virtual host mapped to wwwroot
    bool webViewReady;
    App App => App.Instance;
    // Ctrl+1/2/3 window modes (SPEC.md › Window modes)
    static readonly Dictionary<string, Size> defaultModeSizes = new() { ["desktop"] = new Size(1200, 780), ["vertical"] = new Size(520, 900), ["mini"] = new Size(400, 200) };

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (sender, args) => await InitWebViewAsync();
        SourceInitialized += (sender, args) => { ApplyWindowStyle(); HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ThemeMessageHook); };
        StateChanged += (sender, args) => { UpdateFrameMargin(); PushWindowState(); };
        IsVisibleChanged += (sender, args) => App.Session.SetVisible(IsVisible);
        App.StatusChanged += () => Push(App.StatusJson());
        App.Session.StatusUpdated += () => Push($"{{\"type\":\"nowPlaying\",\"status\":{App.Session.LastStatusJson}}}");
        App.Session.PlayersUpdated += () => Push($"{{\"type\":\"players\",\"players\":{App.Session.LastPlayersJson},\"selected\":{JsonSerializer.Serialize(App.Session.SelectedPlayerId == "" ? App.Settings.PlayerMac : App.Session.SelectedPlayerId)},\"own\":{JsonSerializer.Serialize(App.Settings.PlayerMac)}}}");
        App.Library.Changed += PushLibraryStatus;
        App.Library.IndexUpdated += () => Push("{\"type\":\"indexUpdated\"}");
        // Meter data, only while the window is on screen
        App.Visualiser.LevelsReady += levelsJson => { if (webViewReady) Dispatcher.BeginInvoke(() => { if (IsVisible && WindowState != WindowState.Minimized) webView.CoreWebView2?.PostWebMessageAsJson(levelsJson); }); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (sender, args) => Dispatcher.BeginInvoke(RestoreBoundsForDisplays);
        App.LibraryChanged += () => { Push(JsonSerializer.Serialize(new { type = "settings", library = App.Settings.Library })); Push("{\"type\":\"indexUpdated\"}"); };
        // WebView2 forwards accelerator keys to WPF as KeyDown
        PreviewKeyDown += (sender, keyArgs) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            switch (keyArgs.Key)
            {
                case Key.Q: keyArgs.Handled = true; App.Quit(); break;
                case Key.OemComma: keyArgs.Handled = true; App.OpenSettings(); break;
                case Key.D1: keyArgs.Handled = true; SetWindowMode("desktop"); break;
                case Key.D2: keyArgs.Handled = true; SetWindowMode("vertical"); break;
                case Key.D3: keyArgs.Handled = true; SetWindowMode("mini"); break;
            }
        };
    }

    async Task InitWebViewAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataFolder, "WebView2"));
        await webView.EnsureCoreWebView2Async(environment);
        webView.DefaultBackgroundColor = glassActive || App.Settings.General.Frameless ? System.Drawing.Color.Transparent : System.Drawing.Color.FromArgb(16, 17, 20); // frameless: the page rounds its own corners inside the border
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(uiHost, Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.DenyCors);
        App.Library.AttachArtHandler(webView.CoreWebView2);
        webView.CoreWebView2.WebMessageReceived += async (sender, args) => await OnUiMessageAsync(args.WebMessageAsJson);
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false; // the UI owns Ctrl+F, Ctrl+N etc.
        webView.CoreWebView2.NavigationCompleted += (sender, args) => webViewReady = true;
        // The page's renderer can die (out of memory, GPU reset): log it and reload rather than leave a blank window
        webView.CoreWebView2.ProcessFailed += (sender, args) => { CrashReporter.Write($"WebView2 process failed: {args.ProcessFailedKind} ({args.Reason})", null); if (args.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive) webView.CoreWebView2.Reload(); };
        webView.CoreWebView2.Navigate($"https://{uiHost}/index.html");
    }

    void Push(string messageJson) { if (webViewReady) Dispatcher.BeginInvoke(() => webView.CoreWebView2?.PostWebMessageAsJson(messageJson)); }
    void Reply(JsonElement message, object? data) { if (message.TryGetProperty("id", out var requestId)) Push(JsonSerializer.Serialize(new { type = "result", id = requestId.GetInt64(), data })); }
    void ReplyRaw(JsonElement message, string dataJson) { if (message.TryGetProperty("id", out var requestId)) Push($"{{\"type\":\"result\",\"id\":{requestId.GetInt64()},\"data\":{dataJson}}}"); }

    void PushLibraryStatus() => Push(JsonSerializer.Serialize(new { type = "library", text = App.Library.ProgressText, done = App.Library.ProgressDone, total = App.Library.ProgressTotal }));

    static object[] CommandParts(JsonElement commandArray) => commandArray.EnumerateArray().Select(part => part.ValueKind == JsonValueKind.String ? (object)(part.GetString() ?? "") : part.GetRawText()).ToArray();
    static List<long> Ids(JsonElement idArray) => idArray.EnumerateArray().Select(idElement => idElement.ValueKind == JsonValueKind.Number ? idElement.GetInt64() : long.Parse(idElement.GetString() ?? "0")).ToList();
    static string Text(JsonElement message, string name) => message.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText() : "";

    // Bridge: the UI posts {type, ...}; requests carrying an id get {type:"result", id, data}
    async Task OnUiMessageAsync(string messageJson)
    {
        using var messageDocument = JsonDocument.Parse(messageJson);
        var message = messageDocument.RootElement.Clone();
        webViewReady = true; // the page can post 'ready' before NavigationCompleted fires
        CrashReporter.Note("UI message " + (message.TryGetProperty("type", out var messageType) ? messageType.GetString() : "?"));
        try
        {
            switch (Text(message, "type"))
            {
                case "ready": PushSkin(); PushMeterSettings(); _ = App.ShowWhatsNewAsync(); PushWindowState(); Push(App.StatusJson()); PushLibraryStatus(); Push(JsonSerializer.Serialize(new { type = "settings", library = App.Settings.Library })); break;
                case "openSettings": App.OpenSettings(Text(message, "tab")); break;
                case "quit": App.Quit(); break;
                case "applyUpdate": await App.Updater.RestartToUpdateAsync(); break;
                case "command": await App.Session.RequestAsync(true, CommandParts(message.GetProperty("command"))); break;
                case "live": var liveResult = await App.Session.RequestAsync(message.TryGetProperty("player", out var forPlayer) && forPlayer.GetBoolean(), CommandParts(message.GetProperty("command"))); ReplyRaw(message, liveResult.ValueKind == JsonValueKind.Undefined ? "null" : liveResult.GetRawText()); break;
                case "query":
                    var queryName = Text(message, "name");
                    var queryParameters = message.TryGetProperty("params", out var parameters) ? parameters : default;
                    var queries = App.Library.Queries;
                    Reply(message, await Task.Run(() => queries.Run(queryName, queryParameters)));
                    break;
                case "playTracks": await App.Session.PlayTracksAsync(Ids(message.GetProperty("ids")), Text(message, "mode"), message.TryGetProperty("start", out var start) ? start.GetInt32() : 0); break;
                case "playAlbum":
                    var albumQueries = App.Library.Queries;
                    var albumKey = Text(message, "key");
                    var albumTrackIds = await Task.Run(() => ((List<object?>)albumQueries.Run("albumTrackIds", JsonDocument.Parse(JsonSerializer.Serialize(new { key = albumKey })).RootElement)).Select(trackId => Convert.ToInt64(trackId)).ToList());
                    await App.Session.PlayTracksAsync(albumTrackIds, Text(message, "mode"), message.TryGetProperty("start", out var albumStart) ? albumStart.GetInt32() : 0);
                    break;
                case "selectPlayer": App.Session.SelectedPlayerId = Text(message, "id") == App.Settings.PlayerMac ? "" : Text(message, "id"); await App.Session.RequestAsync(true, "status", "-", 1); break;
                case "savePlaylist":
                    var urlQueries = App.Library.Queries;
                    var idsJson = message.GetProperty("ids").GetRawText();
                    var urlRows = await Task.Run(() => (List<Dictionary<string, object?>>)urlQueries.Run("trackUrls", JsonDocument.Parse(JsonSerializer.Serialize(new { ids = idsJson })).RootElement));
                    var urlById = urlRows.ToDictionary(row => Convert.ToInt64(row["id"]), row => (string)row["url"]!);
                    var orderedUrls = Ids(message.GetProperty("ids")).Where(urlById.ContainsKey).Select(trackId => urlById[trackId]).ToList();
                    await App.Session.SavePlaylistAsync(Text(message, "name"), orderedUrls, message.TryGetProperty("favourite", out var favourite) && favourite.GetBoolean());
                    Reply(message, new { ok = true });
                    break;
                case "addToSavedPlaylist":
                    var savedPlaylistId = Text(message, "playlistId");
                    var addQueries = App.Library.Queries;
                    var addIdsJson = message.GetProperty("ids").GetRawText();
                    var addRows = await Task.Run(() => (List<Dictionary<string, object?>>)addQueries.Run("trackUrls", JsonDocument.Parse(JsonSerializer.Serialize(new { ids = addIdsJson })).RootElement));
                    var addUrlById = addRows.ToDictionary(row => Convert.ToInt64(row["id"]), row => (string)row["url"]!);
                    var countBefore = Server.LyrionClient.ReadInt(await App.Session.RequestAsync(false, "playlists", "tracks", 0, 0, $"playlist_id:{savedPlaylistId}"), "count");
                    foreach (var trackId in Ids(message.GetProperty("ids")).Where(addUrlById.ContainsKey)) await App.Session.RequestAsync(false, "playlists", "edit", "cmd:add", $"playlist_id:{savedPlaylistId}", $"url:{addUrlById[trackId]}");
                    Reply(message, new { before = countBefore });
                    break;
                case "loadDraft": ReplyRaw(message, File.Exists(Shell.PlayerSession.DraftPath) ? File.ReadAllText(Shell.PlayerSession.DraftPath) : "null"); break;
                case "saveDraft": Directory.CreateDirectory(AppPaths.DataFolder); if (Text(message, "draft") == "null") File.Delete(Shell.PlayerSession.DraftPath); else File.WriteAllText(Shell.PlayerSession.DraftPath, message.GetProperty("draft").GetRawText()); break;
                case "openEq": App.OpenEqualiser(); break;
                case "edgeColour": SetEdgeColour(Text(message, "colour")); break;
                // Frameless: the page asks Windows to resize from an edge, as if the native frame were grabbed
                case "resizeWindow": if (resizeHitCodes.TryGetValue(Text(message, "edge"), out var hitCode)) { ReleaseCapture(); SendMessage(new WindowInteropHelper(this).Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, (IntPtr)hitCode, IntPtr.Zero); } break;
                case "dragWindow": ReleaseCapture(); SendMessage(new WindowInteropHelper(this).Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero); webView.Focus(); break; // the drag loop returns on mouse-up; give the page keyboard focus back so Ctrl+C reaches it
                case "windowControl":
                    switch (Text(message, "action")) { case "minimise": WindowState = WindowState.Minimized; break; case "maximise": WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; break; case "close": Close(); break; }
                    break;
                case "theme": darkTheme = message.TryGetProperty("dark", out var darkValue) && darkValue.GetBoolean(); ApplyWindowStyle(); break;
                case "setTopmost": Topmost = message.GetProperty("on").GetBoolean(); break;
                case "windowMode": SetWindowMode(Text(message, "mode")); break;
                case "refreshLibrary": await App.Library.RefreshAsync(message.TryGetProperty("rebuild", out var rebuild) && rebuild.GetBoolean()); break;
            }
        }
        catch (Exception bridgeError) { if (message.TryGetProperty("id", out _)) Reply(message, new { error = bridgeError.Message }); }
    }

    // Window state and bounds handed to the relaunched version after an update
    public string DescribeForRestart()
    {
        var shownState = !IsVisible ? "hidden" : WindowState == WindowState.Maximized ? "maximised" : WindowState == WindowState.Minimized ? "minimised" : "normal";
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        return string.Join(",", new object[] { shownState, bounds.Left, bounds.Top, bounds.Width, bounds.Height }.Select(part => Convert.ToString(part, System.Globalization.CultureInfo.InvariantCulture)));
    }

    public void RestoreFromArgument(string argument)
    {
        var parts = argument.Split(',');
        var numbers = parts.Skip(1).Select(part => double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : double.NaN).ToArray();
        if (numbers.Length == 4 && numbers.All(number => !double.IsNaN(number) && !double.IsInfinity(number)) && numbers[2] > 100 && numbers[3] > 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = numbers[0]; Top = numbers[1]; Width = numbers[2]; Height = numbers[3];
        }
        if (parts[0] == "hidden") return;
        Show();
        if (parts[0] == "maximised") WindowState = WindowState.Maximized; else if (parts[0] == "minimised") WindowState = WindowState.Minimized;
    }

    public void SetWindowMode(string mode)
    {
        if (!defaultModeSizes.TryGetValue(mode, out var defaultSize)) return;
        var modeSizes = App.Settings.General.ModeSizes;
        var currentMode = ActualWidth < 420 && ActualHeight < 260 ? "mini" : ActualWidth <= 900 ? "vertical" : "desktop";
        modeSizes[currentMode] = new[] { Width, Height };
        var targetSize = modeSizes.TryGetValue(mode, out var savedSize) && savedSize.Length == 2 ? new Size(savedSize[0], savedSize[1]) : defaultSize;
        WindowState = WindowState.Normal;
        Width = targetSize.Width; Height = targetSize.Height;
        if (mode != "mini") Topmost = false;
        App.Settings.Save();
    }

    // ---- Glass and frameless (SPEC.md › Look) ----
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr windowHandle, ref FrameMargins margins);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr windowHandle, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr windowHandle, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    const int StyleIndex = -16, CaptionStyle = 0x00C00000, SystemMenuStyle = 0x00080000, CaptionColourAttribute = 35, CornerAttribute = 33, RoundCorners = 2;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr windowHandle, int messageId, IntPtr wParam, IntPtr lParam);
    struct FrameMargins { public int Left, Right, Top, Bottom; }
    const int DarkModeAttribute = 20, BorderColourAttribute = 34, BackdropAttribute = 38, AcrylicBackdrop = 3, NoBackdrop = 1;
    bool glassActive, darkTheme = true;

    public void ApplyWindowStyle()
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero) return;
        var general = App.Settings.General;
        WindowChrome.SetWindowChrome(this, general.Frameless ? new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(-1), UseAeroCaptionButtons = false, CornerRadius = new CornerRadius(8) } : null);
        var darkValue = darkTheme ? 1 : 0;
        DwmSetWindowAttribute(windowHandle, DarkModeAttribute, ref darkValue, sizeof(int));
        // Frameless: without the caption styles DWM draws no title strip or buttons of its own (the thick frame stays, for resizing and the shadow)
        var style = GetWindowLong(windowHandle, StyleIndex);
        var wantedStyle = general.Frameless ? style & ~(CaptionStyle | SystemMenuStyle) : style | CaptionStyle | SystemMenuStyle;
        if (wantedStyle != style) { SetWindowLong(windowHandle, StyleIndex, wantedStyle); SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, 0x0027 /*NOMOVE|NOSIZE|NOZORDER|FRAMECHANGED*/); }
        var captionColour = general.Frameless ? unchecked((int)0xFFFFFFFE) : unchecked((int)0xFFFFFFFF);
        DwmSetWindowAttribute(windowHandle, CaptionColourAttribute, ref captionColour, sizeof(int));
        var corners = RoundCorners;
        DwmSetWindowAttribute(windowHandle, CornerAttribute, ref corners, sizeof(int));
        var borderColour = general.Frameless ? unchecked((int)0xFFFFFFFE) /*DWMWA_COLOR_NONE*/ : unchecked((int)0xFFFFFFFF) /*default*/;
        DwmSetWindowAttribute(windowHandle, BorderColourAttribute, ref borderColour, sizeof(int));
        // Acrylic needs Windows 11 22H2+; elsewhere the call fails and the UI stays opaque
        var backdrop = general.Glass ? AcrylicBackdrop : NoBackdrop;
        glassActive = general.Glass && DwmSetWindowAttribute(windowHandle, BackdropAttribute, ref backdrop, sizeof(int)) == 0;
        var margins = glassActive ? new FrameMargins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new FrameMargins();
        DwmExtendFrameIntoClientArea(windowHandle, ref margins);
        var hwndSource = HwndSource.FromHwnd(windowHandle);
        if (hwndSource?.CompositionTarget != null) hwndSource.CompositionTarget.BackgroundColor = glassActive ? Colors.Transparent : Color.FromRgb(16, 17, 20);
        Background = general.Frameless && edgeColour != null ? new SolidColorBrush(edgeColour.Value) : glassActive ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(16, 17, 20));
        if (webView.CoreWebView2 != null) webView.DefaultBackgroundColor = glassActive || App.Settings.General.Frameless ? System.Drawing.Color.Transparent : System.Drawing.Color.FromArgb(16, 17, 20); // frameless: the page rounds its own corners inside the border
        UpdateFrameMargin();
        PushWindowState();
    }

    // Windows can drop the acrylic backdrop and frame extension when the theme or composition changes; the icon refresh after an update
    // broadcasts exactly that, which left the window black. Re-apply the look whenever one arrives (batched, once things settle)
    bool restyleQueued; DateTime lastRestyle;
    IntPtr ThemeMessageHook(IntPtr windowHandle, int messageId, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SETTINGCHANGE = 0x001A, WM_THEMECHANGED = 0x031A, WM_DWMCOMPOSITIONCHANGED = 0x031E, WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
        if (messageId is WM_SETTINGCHANGE or WM_THEMECHANGED or WM_DWMCOMPOSITIONCHANGED or WM_DWMCOLORIZATIONCOLORCHANGED && !restyleQueued && DateTime.UtcNow - lastRestyle > TimeSpan.FromSeconds(2)) // never loops on our own restyle
        {
            restyleQueued = true;
            Dispatcher.BeginInvoke(async () => { await Task.Delay(400); restyleQueued = false; lastRestyle = DateTime.UtcNow; ApplyWindowStyle(); PushSkin(); }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        return IntPtr.Zero;
    }

    // The thin frameless edge around the page takes the page's own background (tint included on glass), so it reads as part of the page and
    // the corners are just the window's rounded ones, not a ring with a different inner curve
    static readonly Dictionary<string, int> resizeHitCodes = new() { ["left"] = 10, ["right"] = 11, ["top"] = 12, ["topleft"] = 13, ["topright"] = 14, ["bottom"] = 15, ["bottomleft"] = 16, ["bottomright"] = 17 };
    Color? edgeColour;
    void SetEdgeColour(string cssColour)
    {
        var parts = System.Text.RegularExpressions.Regex.Matches(cssColour, @"[\d.]+").Select(match => double.Parse(match.Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        if (parts.Count < 3) return;
        edgeColour = Color.FromArgb((byte)Math.Round((parts.Count > 3 ? parts[3] : 1) * 255), (byte)parts[0], (byte)parts[1], (byte)parts[2]);
        if (App.Settings.General.Frameless) Background = new SolidColorBrush(edgeColour.Value);
    }

    // Frameless: the page fills the window to its edge, so Windows' rounded corners clip it cleanly (resizing comes from the page's edge strips);
    // maximised windows overhang the screen, so keep that overhang clear
    void UpdateFrameMargin() => webView.Margin = App.Settings.General.Frameless && WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    public void PushNewPlaylist() => Push("{\"type\":\"newPlaylist\"}");

    // ---- Multi-monitor: size and position are remembered per monitor layout, and a window left off-screen comes back ----
    static string DisplayKey => string.Join("|", System.Windows.Forms.Screen.AllScreens.Select(screen => $"{screen.Bounds.X},{screen.Bounds.Y},{screen.Bounds.Width}x{screen.Bounds.Height}").OrderBy(text => text));
    public void SaveBounds()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.IsEmpty || double.IsNaN(bounds.Width) || double.IsInfinity(bounds.Width) || bounds.Width < 200) return;
        App.Settings.General.WindowBounds[DisplayKey] = new[] { bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized ? 1 : 0 };
    }

    public void RestoreBoundsForDisplays()
    {
        if (App.Settings.General.WindowBounds.TryGetValue(DisplayKey, out var saved) && saved.Length == 5 && saved[2] >= 200 && saved[3] >= 150)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            Left = saved[0]; Top = saved[1]; Width = saved[2]; Height = saved[3];
            if (saved[4] == 1) WindowState = WindowState.Maximized;
        }
        KeepOnScreen();
    }

    // The top strip (where you drag) must be on some screen's working area; otherwise move onto the primary screen
    void KeepOnScreen()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var dragPoint = new Point(Left + Math.Min(Width, 400) / 2, Top + 12);
        var onScreen = System.Windows.Forms.Screen.AllScreens.Any(screen => new Rect(screen.WorkingArea.X / scale, screen.WorkingArea.Y / scale, screen.WorkingArea.Width / scale, screen.WorkingArea.Height / scale).Contains(dragPoint));
        if (onScreen) return;
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, workArea.Width); Height = Math.Min(Height, workArea.Height);
        Left = workArea.Left + (workArea.Width - Width) / 2; Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    public void PushSkin() => Push(JsonSerializer.Serialize(new { type = "skin", skin = App.Settings.General.Skin, coverAccent = App.Settings.General.CoverAccent, coverBackdrop = App.Settings.General.CoverBackdrop, accent = App.Settings.General.AccentColour, glassTransparency = App.Settings.General.GlassTransparency, glassTintHue = App.Settings.General.GlassTintHue }));
    public void PushWhatsNew(string fromVersion, string toVersion, List<ReleaseEntry> releases) => Push(JsonSerializer.Serialize(new { type = "whatsNew", fromVersion, toVersion, releases = releases.Select(release => new { version = release.Version, date = release.Published.ToString("d MMM"), notes = release.Notes, url = release.Url }) }));
    public void PushMeterSettings() => Push(JsonSerializer.Serialize(new { type = "meterSettings", referenceDbfs = App.Settings.Meters.ReferenceDbfs, spectrumFloorDbfs = App.Settings.Meters.SpectrumFloorDbfs, spectrumCeilingDbfs = App.Settings.Meters.SpectrumCeilingDbfs }));
    void PushWindowState() => Push(JsonSerializer.Serialize(new { type = "window", glass = glassActive, frameless = App.Settings.General.Frameless, maximised = WindowState == WindowState.Maximized }));

    void OnPreviousThumb(object sender, EventArgs args) => _ = App.Session.RequestAsync(true, "playlist", "index", "-1");
    void OnPlayPauseThumb(object sender, EventArgs args) => _ = App.Session.RequestAsync(true, "pause");
    void OnNextThumb(object sender, EventArgs args) => _ = App.Session.RequestAsync(true, "playlist", "index", "+1");

    protected override void OnClosing(CancelEventArgs closingArgs)
    {
        if (App.IsQuitting) return;
        closingArgs.Cancel = true;
        if (!App.Settings.General.CloseHidesToTray) { App.Quit(); return; }
        SaveBounds(); App.Settings.Save();
        Hide();
        if (!App.Settings.General.CloseToTrayTipShown)
        {
            App.ShowTip("WinSqueeze is still playing", "Closing the window hides it to the tray. Quit from the tray menu.");
            App.Settings.General.CloseToTrayTipShown = true; App.Settings.Save();
        }
    }
}
