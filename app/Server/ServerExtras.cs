using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WinSqueeze.Settings;
namespace WinSqueeze.Server;

// Lyrion's Extras (SPEC.md › Radio and apps): plugins that bring their own web pages (Dynamic Playlists, Virtual Library Creator, Info Browser…).
// Lyrion only lists them on its web home page, so they are read from the Default skin's Extras section and each page opens in a window of its own.
public static class ServerExtras
{
    static readonly HttpClient webClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly HashSet<string> notPages = new() { "ALARM", "PLUGIN_LINE_IN", "PLUGIN_LINE_OUT" }; // settings shortcuts and switches, not plugin pages
    static readonly Regex menuItem = new("<div class=\"homeMenuItem\\s*\" id=\"([^\"]+)\">.*?<a href=\"([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
    public static async Task<object[]> ListAsync(string playerId)
    {
        var profile = App.Instance.Settings.ActiveProfile;
        if (App.Instance.IsOffline || profile is not { Host.Length: > 0 }) return Array.Empty<object>();
        var serverRoot = $"http://{profile.Host}:{profile.Port}/";
        string homePage;
        try { homePage = await webClient.GetStringAsync($"{serverRoot}Default/home.html?player={Uri.EscapeDataString(playerId)}"); } catch { return Array.Empty<object>(); }
        var sectionStart = homePage.IndexOf("id=\"PLUGINS_expanded\"", StringComparison.Ordinal);
        if (sectionStart < 0) return Array.Empty<object>();
        return menuItem.Matches(homePage[sectionStart..]).Select(match => (key: match.Groups[1].Value, href: WebUtility.HtmlDecode(match.Groups[2].Value), title: WebUtility.HtmlDecode(Regex.Replace(match.Groups[3].Value, "<[^>]+>", " ")).Trim(), icon: Regex.Match(match.Groups[3].Value, "<img src=\"([^\"]+)\"").Groups[1].Value))
            .Where(entry => entry.title != "" && !notPages.Contains(entry.key) && !entry.href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            .Select(entry => (object)new { key = entry.key, title = entry.title, url = entry.href.StartsWith("http") ? entry.href : serverRoot + entry.href.TrimStart('/'), icon = WebUtility.HtmlDecode(entry.icon).Replace("_25x25", "_200x200") }).ToArray();
    }
    // Only the connected server's own pages open here; links the page opens in a new window stay in this one
    public static void Open(string title, string url)
    {
        var profile = App.Instance.Settings.ActiveProfile;
        if (profile is not { Host.Length: > 0 } || !Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != "http" || address.Host != profile.Host) return;
        var webView = new WebView2();
        var window = new Window { Title = title, Width = 1000, Height = 760, MinWidth = 400, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = webView, Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/WinSqueeze.ico")) };
        window.Loaded += async (sender, args) =>
        {
            await webView.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataFolder, "WebView2")));
            webView.CoreWebView2.NewWindowRequested += (newSender, newArgs) => { newArgs.Handled = true; webView.CoreWebView2.Navigate(newArgs.Uri); };
            webView.CoreWebView2.Navigate(address.AbsoluteUri);
        };
        window.Show();
    }
}
