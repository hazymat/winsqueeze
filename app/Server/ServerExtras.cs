using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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
    record Extra(string key, string title, string url, string icon);
    // Two sources, joined: the Default skin's home page (always there), and Material Skin's own list when it is installed,
    // which also carries the links plugins register for iPeng only (SugarCube, RandomFlow…)
    public static async Task<object[]> ListAsync(string playerId)
    {
        var profile = App.Instance.Settings.ActiveProfile;
        if (App.Instance.IsOffline || profile is not { Host.Length: > 0 }) return Array.Empty<object>();
        var serverRoot = $"http://{profile.Host}:{profile.Port}/";
        string Absolute(string href) => href.StartsWith("http") ? href : serverRoot + href.TrimStart('/');
        var extras = new List<Extra>();
        try
        {
            var homePage = await webClient.GetStringAsync($"{serverRoot}Default/home.html?player={Uri.EscapeDataString(playerId)}");
            var sectionStart = homePage.IndexOf("id=\"PLUGINS_expanded\"", StringComparison.Ordinal);
            if (sectionStart >= 0) extras.AddRange(menuItem.Matches(homePage[sectionStart..]).Select(match => new Extra(match.Groups[1].Value, WebUtility.HtmlDecode(Regex.Replace(match.Groups[3].Value, "<[^>]+>", " ")).Trim(), Absolute(WebUtility.HtmlDecode(match.Groups[2].Value)), WebUtility.HtmlDecode(Regex.Match(match.Groups[3].Value, "<img src=\"([^\"]+)\"").Groups[1].Value).Replace("_25x25", "_200x200"))));
        }
        catch { }
        try
        {
            var materialList = await App.Instance.ServerRequestAsync(playerId, "material-skin", "extras");
            if (materialList.ValueKind == JsonValueKind.Object && materialList.TryGetProperty("extras_loop", out var materialExtras) && materialExtras.ValueKind == JsonValueKind.Array)
                foreach (var entry in materialExtras.EnumerateArray())
                {
                    string Field(string name) => entry.TryGetProperty(name, out var value) ? value.ToString() : "";
                    if (Field("id") != "" && Field("url") != "" && !extras.Any(known => known.key == Field("id"))) extras.Add(new Extra(Field("id"), Field("title"), Absolute(Field("url") + "player=" + Uri.EscapeDataString(playerId)), Field("icon")));
                }
        }
        catch { }
        return extras.Where(extra => extra.title != "" && !notPages.Contains(extra.key) && !extra.url.Contains("javascript:", StringComparison.OrdinalIgnoreCase)).OrderBy(extra => extra.title, StringComparer.CurrentCultureIgnoreCase).Cast<object>().ToArray();
    }
    // Only the connected server's own pages open here; links the page opens in a new window stay in this one
    public static void Open(string title, string url)
    {
        var profile = App.Instance.Settings.ActiveProfile;
        if (profile is not { Host.Length: > 0 } || !Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != "http" || address.Host != profile.Host) return;
        var webView = new WebView2();
        // Sized to fit the screen the main window is on (small laptop screens at 125-150% scaling are only ~600 px tall), centred on it
        var mainWindow = App.Instance.MainWindow;
        var handle = mainWindow == null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
        var screenArea = (handle == IntPtr.Zero ? System.Windows.Forms.Screen.PrimaryScreen! : System.Windows.Forms.Screen.FromHandle(handle)).WorkingArea;
        var scale = mainWindow == null ? 1 : System.Windows.Media.VisualTreeHelper.GetDpi(mainWindow).DpiScaleX;
        var workArea = new Rect(screenArea.X / scale, screenArea.Y / scale, screenArea.Width / scale, screenArea.Height / scale);
        double width = Math.Min(1000, workArea.Width * 0.92), height = Math.Min(760, workArea.Height * 0.92);
        var window = new Window { Title = title, Width = width, Height = height, MinWidth = Math.Min(400, width), MinHeight = Math.Min(300, height), WindowStartupLocation = WindowStartupLocation.Manual, Left = workArea.Left + (workArea.Width - width) / 2, Top = workArea.Top + (workArea.Height - height) / 2, Content = webView, Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/WinSqueeze.ico")) };
        window.Loaded += async (sender, args) =>
        {
            await webView.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataFolder, "WebView2")));
            webView.CoreWebView2.NewWindowRequested += (newSender, newArgs) => { newArgs.Handled = true; webView.CoreWebView2.Navigate(newArgs.Uri); };
            webView.CoreWebView2.Navigate(address.AbsoluteUri);
        };
        window.Show();
    }
}
