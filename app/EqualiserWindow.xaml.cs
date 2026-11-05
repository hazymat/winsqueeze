using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using WinSqueeze.Audio;
using WinSqueeze.Settings;
using WinSqueeze.Shell;
namespace WinSqueeze;

// The graphic equaliser's own window (SPEC.md › Equaliser). It keeps its own size, snaps magnetically to any edge of the main
// window and then moves with it; dragging it away undocks it.
public partial class EqualiserWindow : Window
{
    const int SnapPixels = 20, SizeSnapPixels = 12;
    bool webViewReady;
    App App => App.Instance;
    EqualiserSettings Tuning => App.Settings.Equaliser;
    IntPtr Handle => new WindowInteropHelper(this).Handle;
    IntPtr MainHandle => new WindowInteropHelper(Owner).Handle;

    public EqualiserWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        if (Tuning.WindowWidth == 980) Tuning.WindowWidth = 1200; // the first release's default was too narrow
        Width = Math.Max(MinWidth, Tuning.WindowWidth); Height = Math.Max(MinHeight, Tuning.WindowHeight);
        if (Tuning.WindowLeft is double savedLeft && Tuning.WindowTop is double savedTop && !double.IsNaN(savedLeft)) { WindowStartupLocation = WindowStartupLocation.Manual; Left = savedLeft; Top = savedTop; }
        else WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SourceInitialized += (sender, args) => { HwndSource.FromHwnd(Handle).AddHook(WindowMessageHook); FollowOwner(); };
        Loaded += async (sender, args) => await InitWebViewAsync();
        Closing += (sender, args) => SaveBounds();
        EventHandler follow = (sender, args) => FollowOwner();
        SizeChangedEventHandler followSize = (sender, args) => FollowOwner();
        DependencyPropertyChangedEventHandler showWithOwner = (sender, args) => { if (owner.IsVisible) Show(); else Hide(); };
        owner.LocationChanged += follow; owner.SizeChanged += followSize; owner.IsVisibleChanged += showWithOwner;
        App.Visualiser.AnalyserReady += OnAnalyser;
        Closed += (sender, args) => { owner.LocationChanged -= follow; owner.SizeChanged -= followSize; owner.IsVisibleChanged -= showWithOwner; App.Visualiser.AnalyserReady -= OnAnalyser; App.Visualiser.AnalyserWanted = false; };
        App.Visualiser.AnalyserWanted = true;
        // CLIP lamp: how hard the limiter worked in the last 100 ms
        var clipTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        clipTimer.Tick += (sender, args) => { if (webViewReady && IsVisible) webView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "eqClip", reductionDb = App.Equaliser.TakeLimiterReductionDb() })); };
        clipTimer.Start();
        Closed += (sender, args) => clipTimer.Stop();
    }

    void OnAnalyser(string levelsJson) { if (webViewReady) Dispatcher.BeginInvoke(() => { if (IsVisible) webView.CoreWebView2?.PostWebMessageAsJson(levelsJson); }); }

    async Task InitWebViewAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataFolder, "WebView2"));
        await webView.EnsureCoreWebView2Async(environment);
        webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(11, 11, 12);
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping("app.winsqueeze", Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.DenyCors);
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
        webView.CoreWebView2.WebMessageReceived += (sender, args) => OnUiMessage(args.WebMessageAsJson);
        webView.CoreWebView2.Navigate("https://app.winsqueeze/index.html#eq");
    }

    public void PushState() { if (webViewReady) webView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "eq", eq = Tuning, supported = SqueezeliteHost.SupportsEqualiser, rate = App.Visualiser.SampleRate })); }

    void OnUiMessage(string messageJson)
    {
        using var messageDocument = JsonDocument.Parse(messageJson);
        var message = messageDocument.RootElement;
        try
        {
            switch (message.GetProperty("type").GetString())
            {
                case "ready": webViewReady = true; PushState(); webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "eqPreamp", autoPreampDb = App.Equaliser.AutoPreampDb })); break;
                // The panel sends the whole EQ state; Commit (pointer up, button press) also saves it to disk
                case "eqSet":
                    var incoming = message.GetProperty("eq").Deserialize<EqualiserSettings>();
                    if (incoming == null) break;
                    var tuning = Tuning; var wasEnabled = tuning.Enabled;
                    tuning.Enabled = incoming.Enabled; tuning.BandCount = incoming.BandCount; tuning.Character = incoming.Character; tuning.Linked = incoming.Linked; tuning.PreampDb = incoming.PreampDb; tuning.AutoPreamp = incoming.AutoPreamp; tuning.Gains = incoming.Gains;
                    tuning.Display = incoming.Display; tuning.Decay = incoming.Decay; tuning.EditMode = incoming.EditMode;
                    App.Equaliser.Changed();
                    if (message.TryGetProperty("commit", out var commit) && commit.GetBoolean()) App.Settings.Save();
                    if (wasEnabled != tuning.Enabled) App.RaiseStatusChanged();
                    webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "eqPreamp", autoPreampDb = App.Equaliser.AutoPreampDb }));
                    break;
                case "dragWindow": ReleaseCapture(); SendMessage(Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero); webView.Focus(); break;
                case "close": Close(); break;
                case "resizeWindow": if (resizeHitCodes.TryGetValue(message.GetProperty("edge").GetString() ?? "", out var hitCode)) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)hitCode, IntPtr.Zero); } break;
            }
        }
        catch (Exception error) { CrashReporter.Write("Equaliser window message failed", error, false); }
    }

    static readonly Dictionary<string, int> resizeHitCodes = new() { ["left"] = 10, ["right"] = 11, ["top"] = 12, ["topleft"] = 13, ["topright"] = 14, ["bottom"] = 15, ["bottomleft"] = 16, ["bottomright"] = 17 };

    void SaveBounds()
    {
        Tuning.WindowLeft = Left; Tuning.WindowTop = Top; Tuning.WindowWidth = Width; Tuning.WindowHeight = Height;
        App.Settings.Save();
    }

    // ---- Docking ----
    // While dragged: pull the window flush against any main-window edge it comes within SnapPixels of (and line the ends up when close)
    IntPtr WindowMessageHook(IntPtr windowHandle, int messageId, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOVING = 0x0216, WM_SIZING = 0x0214, WM_EXITSIZEMOVE = 0x0232;
        if (messageId == WM_MOVING && Owner.IsVisible)
        {
            var moving = Marshal.PtrToStructure<Rect32>(lParam);
            GetWindowRect(MainHandle, out var main);
            int width = moving.Right - moving.Left, height = moving.Bottom - moving.Top;
            bool overlapsVertically = moving.Bottom > main.Top && moving.Top < main.Bottom, overlapsHorizontally = moving.Right > main.Left && moving.Left < main.Right;
            if (overlapsVertically && Math.Abs(moving.Left - main.Right) < SnapPixels) moving.Left = main.Right;
            else if (overlapsVertically && Math.Abs(moving.Right - main.Left) < SnapPixels) moving.Left = main.Left - width;
            else if (overlapsHorizontally && Math.Abs(moving.Top - main.Bottom) < SnapPixels) moving.Top = main.Bottom;
            else if (overlapsHorizontally && Math.Abs(moving.Bottom - main.Top) < SnapPixels) moving.Top = main.Top - height;
            // Line the ends up with the main window's when nearly aligned
            if (Math.Abs(moving.Top - main.Top) < SnapPixels) moving.Top = main.Top; else if (Math.Abs(moving.Top + height - main.Bottom) < SnapPixels) moving.Top = main.Bottom - height;
            if (Math.Abs(moving.Left - main.Left) < SnapPixels) moving.Left = main.Left; else if (Math.Abs(moving.Left + width - main.Right) < SnapPixels) moving.Left = main.Right - width;
            moving.Right = moving.Left + width; moving.Bottom = moving.Top + height;
            Marshal.StructureToPtr(moving, lParam, false);
            handled = true; return (IntPtr)1;
        }
        // While resized when docked: an edge being dragged snaps to the main window's matching edge, so width or height matches and the ends line up
        if (messageId == WM_SIZING && Tuning.DockSide != "" && Owner.IsVisible)
        {
            var sizing = Marshal.PtrToStructure<Rect32>(lParam);
            GetWindowRect(MainHandle, out var main);
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            int minimumWidth = (int)(MinWidth * dpi.DpiScaleX), minimumHeight = (int)(MinHeight * dpi.DpiScaleY), edge = (int)wParam;
            bool leftEdge = edge is 1 or 4 or 7, rightEdge = edge is 2 or 5 or 8, topEdge = edge is 3 or 4 or 5, bottomEdge = edge is 6 or 7 or 8;
            if (leftEdge && Math.Abs(sizing.Left - main.Left) < SizeSnapPixels && sizing.Right - main.Left >= minimumWidth) sizing.Left = main.Left;
            if (rightEdge && Math.Abs(sizing.Right - main.Right) < SizeSnapPixels && main.Right - sizing.Left >= minimumWidth) sizing.Right = main.Right;
            if (topEdge && Math.Abs(sizing.Top - main.Top) < SizeSnapPixels && sizing.Bottom - main.Top >= minimumHeight) sizing.Top = main.Top;
            if (bottomEdge && Math.Abs(sizing.Bottom - main.Bottom) < SizeSnapPixels && main.Bottom - sizing.Top >= minimumHeight) sizing.Bottom = main.Bottom;
            Marshal.StructureToPtr(sizing, lParam, false);
            handled = true; return (IntPtr)1;
        }
        if (messageId == WM_EXITSIZEMOVE) { UpdateDock(); SaveBounds(); }
        return IntPtr.Zero;
    }

    // Docked when flush against an edge; remember the offset along it so the window keeps its place as the main window moves or resizes
    void UpdateDock()
    {
        GetWindowRect(Handle, out var own); GetWindowRect(MainHandle, out var main);
        bool overlapsVertically = own.Bottom > main.Top && own.Top < main.Bottom, overlapsHorizontally = own.Right > main.Left && own.Left < main.Right;
        (Tuning.DockSide, Tuning.DockOffset) =
            overlapsVertically && own.Left == main.Right ? ("right", own.Top - main.Top) :
            overlapsVertically && own.Right == main.Left ? ("left", own.Top - main.Top) :
            overlapsHorizontally && own.Top == main.Bottom ? ("bottom", own.Left - main.Left) :
            overlapsHorizontally && own.Bottom == main.Top ? ("top", own.Left - main.Left) : ("", 0);
    }

    void FollowOwner()
    {
        if (Tuning.DockSide == "" || Handle == IntPtr.Zero || Owner.WindowState == WindowState.Minimized) return;
        GetWindowRect(Handle, out var own); GetWindowRect(MainHandle, out var main);
        int width = own.Right - own.Left, height = own.Bottom - own.Top;
        var (left, top) = Tuning.DockSide switch
        {
            "right" => (main.Right, main.Top + Tuning.DockOffset),
            "left" => (main.Left - width, main.Top + Tuning.DockOffset),
            "bottom" => (main.Left + Tuning.DockOffset, main.Bottom),
            _ => (main.Left + Tuning.DockOffset, main.Top - height),
        };
        SetWindowPos(Handle, IntPtr.Zero, left, top, 0, 0, 0x0001 /*NOSIZE*/ | 0x0004 /*NOZORDER*/ | 0x0010 /*NOACTIVATE*/);
    }

    [StructLayout(LayoutKind.Sequential)] struct Rect32 { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr windowHandle, out Rect32 rectangle);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr windowHandle, int messageId, IntPtr wParam, IntPtr lParam);
}
