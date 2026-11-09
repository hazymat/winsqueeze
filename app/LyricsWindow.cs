using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WinSqueeze.Settings;
using WinSqueeze.Shell;
namespace WinSqueeze;

// Lyrics on their own (SPEC.md › Lyrics view): full screen on the main window's screen, or popped out of the mini player into a window of their own.
// The page follows the playing track line by line; Esc leaves full screen (and closes it when it was opened full screen)
public sealed class LyricsWindow : Window
{
    readonly WebView2 webView = new();
    bool webViewReady, fullScreen;
    public bool OpenedFullScreen; // from the big player: leaving full screen closes it
    App App => App.Instance;
    IntPtr Handle => new WindowInteropHelper(this).Handle;

    public LyricsWindow()
    {
        Title = L.T("WinSqueeze lyrics"); Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/WinSqueeze.ico"));
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(14, 16, 21)); MinWidth = 320; MinHeight = 240; Content = webView;
        var saved = App.Settings.General.LyricsBounds;
        if (saved.Length == 4 && saved[2] >= 320 && saved[3] >= 240) { WindowStartupLocation = WindowStartupLocation.Manual; Left = saved[0]; Top = saved[1]; Width = saved[2]; Height = saved[3]; }
        else { Width = 560; Height = 720; WindowStartupLocation = WindowStartupLocation.CenterScreen; }
        SourceInitialized += (sender, args) => { var dark = 1; DwmSetWindowAttribute(Handle, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref dark, sizeof(int)); };
        Loaded += async (sender, args) => await InitWebViewAsync();
        Action pushStatus = () => Push($"{{\"type\":\"nowPlaying\",\"status\":{App.Session.LastStatusJson}}}");
        App.Session.StatusUpdated += pushStatus;
        IsVisibleChanged += (sender, args) => App.UpdatePolling();
        Closing += (sender, args) => SaveBounds();
        Closed += (sender, args) => { App.Session.StatusUpdated -= pushStatus; App.UpdatePolling(); };
    }

    async Task InitWebViewAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataFolder, "WebView2"));
        await webView.EnsureCoreWebView2Async(environment);
        webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(14, 16, 21);
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping("app.winsqueeze", Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.DenyCors);
        App.Library.AttachArtHandler(webView.CoreWebView2);
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
        webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
        webView.ZoomFactor = Math.Clamp(App.Settings.General.UiScale, 0.75, 1.5);
        webView.CoreWebView2.WebMessageReceived += async (sender, args) => await OnUiMessageAsync(args.WebMessageAsJson);
        webView.CoreWebView2.Navigate($"https://app.winsqueeze/index.html?lang={L.Code}#lyrics");
    }

    void Push(string messageJson) { if (webViewReady) Dispatcher.BeginInvoke(() => webView.CoreWebView2?.PostWebMessageAsJson(messageJson)); }
    void PushState() => Push(JsonSerializer.Serialize(new { type = "lyricsWindow", fullScreen }));

    async Task OnUiMessageAsync(string messageJson)
    {
        using var messageDocument = JsonDocument.Parse(messageJson);
        var message = messageDocument.RootElement.Clone();
        try
        {
            switch (message.GetProperty("type").GetString())
            {
                case "ready": webViewReady = true; PushState(); Push($"{{\"type\":\"nowPlaying\",\"status\":{App.Session.LastStatusJson}}}"); break;
                case "query":
                    var result = message.GetProperty("name").GetString() == "nowPlayingInfo" ? (await App.TrackInfo.LookupAsync(TrackInfo.RequestFrom(message.GetProperty("params")))).ToJson() : null;
                    Push(JsonSerializer.Serialize(new { type = "result", id = message.GetProperty("id").GetInt64(), data = result }));
                    break;
                case "command": await App.Session.RequestAsync(true, message.GetProperty("command").EnumerateArray().Select(part => part.ValueKind == JsonValueKind.String ? (object)(part.GetString() ?? "") : part.GetRawText()).ToArray()); break;
                case "fullScreen": SetFullScreen(message.GetProperty("on").GetBoolean()); break;
                case "close": Close(); break;
            }
        }
        catch (Exception error) { CrashReporter.Write("Lyrics window message failed", error, false); }
    }

    // Full screen: no frame, maximised over the taskbar on the screen the window is on
    public void SetFullScreen(bool on)
    {
        if (!on && OpenedFullScreen) { Close(); return; }
        if (on == fullScreen) { Activate(); return; }
        if (on) SaveBounds();
        fullScreen = on;
        WindowState = WindowState.Normal;
        WindowStyle = on ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        ResizeMode = on ? ResizeMode.NoResize : ResizeMode.CanResize;
        if (on) WindowState = WindowState.Maximized;
        Activate(); PushState();
    }

    // Full screen opens on the screen the main window is on
    public void PlaceOn(Window near)
    {
        if (near.WindowState == WindowState.Minimized || !near.IsVisible) return;
        var bounds = near.WindowState == WindowState.Normal ? new Rect(near.Left, near.Top, near.ActualWidth, near.ActualHeight) : near.RestoreBounds;
        WindowStartupLocation = WindowStartupLocation.Manual; Left = bounds.Left + Math.Max(0, (bounds.Width - Width) / 2); Top = bounds.Top + Math.Max(0, (bounds.Height - Height) / 2);
    }

    void SaveBounds()
    {
        if (fullScreen || WindowState != WindowState.Normal || OpenedFullScreen) return;
        App.Settings.General.LyricsBounds = new[] { Left, Top, Width, Height }; App.Settings.Save();
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int size);
}
