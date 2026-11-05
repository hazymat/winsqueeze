using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using WinSqueeze.Settings;
namespace WinSqueeze;

public partial class MainWindow : Window
{
    const string uiHost = "app.winsqueeze"; // virtual host mapped to wwwroot
    bool webViewReady;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (sender, args) => await InitWebViewAsync();
        App.Instance.StatusChanged += PushStatus;
        // WebView2 forwards accelerator keys to WPF as KeyDown
        PreviewKeyDown += (sender, keyArgs) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (keyArgs.Key == Key.Q) { keyArgs.Handled = true; App.Instance.Quit(); }
            else if (keyArgs.Key == Key.OemComma) { keyArgs.Handled = true; App.Instance.OpenSettings(); }
        };
    }

    async Task InitWebViewAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataFolder, "WebView2"));
        await webView.EnsureCoreWebView2Async(environment);
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(uiHost, Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.DenyCors);
        webView.CoreWebView2.WebMessageReceived += async (sender, args) => await OnUiMessageAsync(args.WebMessageAsJson);
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.CoreWebView2.NavigationCompleted += (sender, args) => { webViewReady = true; PushStatus(); };
        webView.CoreWebView2.Navigate($"https://{uiHost}/index.html");
    }

    void PushStatus() { if (webViewReady) webView.CoreWebView2.PostWebMessageAsJson(App.Instance.StatusJson()); }

    // Bridge: the UI posts {type, ...}
    async Task OnUiMessageAsync(string messageJson)
    {
        using var messageDocument = JsonDocument.Parse(messageJson);
        var message = messageDocument.RootElement;
        switch (message.GetProperty("type").GetString())
        {
            case "ready": PushStatus(); break;
            case "openSettings": App.Instance.OpenSettings(message.TryGetProperty("tab", out var tabName) ? tabName.GetString() ?? "" : ""); break;
            case "quit": App.Instance.Quit(); break;
            case "applyUpdate": await App.Instance.Updater.RestartToUpdateAsync(); break;
            case "command": await App.Instance.SendPlayerCommandAsync(message.GetProperty("command").EnumerateArray().Select(part => (object)(part.GetString() ?? "")).ToArray()); break;
        }
    }

    protected override void OnClosing(CancelEventArgs closingArgs)
    {
        var app = App.Instance;
        if (app.IsQuitting) return;
        closingArgs.Cancel = true;
        if (!app.Settings.General.CloseHidesToTray) { app.Quit(); return; }
        Hide();
        if (!app.Settings.General.CloseToTrayTipShown)
        {
            app.ShowTip("WinSqueeze is still playing", "Closing the window hides it to the tray. Quit from the tray menu.");
            app.Settings.General.CloseToTrayTipShown = true; app.Settings.Save();
        }
    }
}
