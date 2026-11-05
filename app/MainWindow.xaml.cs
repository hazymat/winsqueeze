using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
namespace WinSqueeze;

public partial class MainWindow : Window
{
    const string uiHost = "app.winsqueeze"; // virtual host mapped to wwwroot
    public MainWindow() { InitializeComponent(); Loaded += async (sender, args) => await InitWebViewAsync(); }

    async Task InitWebViewAsync()
    {
        var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinSqueeze", "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await webView.EnsureCoreWebView2Async(environment);
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(uiHost, wwwroot, CoreWebView2HostResourceAccessKind.DenyCors);
        webView.CoreWebView2.WebMessageReceived += (sender, args) => OnUiMessage(args.WebMessageAsJson);
        webView.CoreWebView2.Navigate($"https://{uiHost}/index.html");
    }

    // Bridge: the UI posts {type, ...}; the shell replies with PostWebMessageAsJson
    void OnUiMessage(string messageJson) { webView.CoreWebView2.PostWebMessageAsJson($"{{\"type\":\"echo\",\"received\":{messageJson}}}"); }
}
