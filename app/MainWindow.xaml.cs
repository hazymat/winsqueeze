using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using WinSqueeze.Library;
using WinSqueeze.Settings;
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
        IsVisibleChanged += (sender, args) => App.Session.SetVisible(IsVisible);
        App.StatusChanged += () => Push(App.StatusJson());
        App.Session.StatusUpdated += () => Push($"{{\"type\":\"nowPlaying\",\"status\":{App.Session.LastStatusJson}}}");
        App.Session.PlayersUpdated += () => Push($"{{\"type\":\"players\",\"players\":{App.Session.LastPlayersJson},\"selected\":{JsonSerializer.Serialize(App.Session.SelectedPlayerId == "" ? App.Settings.PlayerMac : App.Session.SelectedPlayerId)},\"own\":{JsonSerializer.Serialize(App.Settings.PlayerMac)}}}");
        App.Library.Changed += PushLibraryStatus;
        App.Library.IndexUpdated += () => Push("{\"type\":\"indexUpdated\"}");
        App.LibraryChanged += () => Push("{\"type\":\"indexUpdated\"}");
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
        webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(16, 17, 20);
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(uiHost, Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.DenyCors);
        App.Library.AttachArtHandler(webView.CoreWebView2);
        webView.CoreWebView2.WebMessageReceived += async (sender, args) => await OnUiMessageAsync(args.WebMessageAsJson);
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false; // the UI owns Ctrl+F, Ctrl+N etc.
        webView.CoreWebView2.NavigationCompleted += (sender, args) => webViewReady = true;
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
        try
        {
            switch (Text(message, "type"))
            {
                case "ready": Push(App.StatusJson()); PushLibraryStatus(); Push(JsonSerializer.Serialize(new { type = "settings", library = App.Settings.Library })); break;
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
                case "loadDraft": ReplyRaw(message, File.Exists(Shell.PlayerSession.DraftPath) ? File.ReadAllText(Shell.PlayerSession.DraftPath) : "null"); break;
                case "saveDraft": Directory.CreateDirectory(AppPaths.DataFolder); if (Text(message, "draft") == "null") File.Delete(Shell.PlayerSession.DraftPath); else File.WriteAllText(Shell.PlayerSession.DraftPath, message.GetProperty("draft").GetRawText()); break;
                case "setTopmost": Topmost = message.GetProperty("on").GetBoolean(); break;
                case "windowMode": SetWindowMode(Text(message, "mode")); break;
                case "refreshLibrary": await App.Library.RefreshAsync(message.TryGetProperty("rebuild", out var rebuild) && rebuild.GetBoolean()); break;
            }
        }
        catch (Exception bridgeError) { if (message.TryGetProperty("id", out _)) Reply(message, new { error = bridgeError.Message }); }
    }

    void SetWindowMode(string mode)
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

    void OnPreviousThumb(object sender, EventArgs args) => _ = App.Session.RequestAsync(true, "playlist", "index", "-1");
    void OnPlayPauseThumb(object sender, EventArgs args) => _ = App.Session.RequestAsync(true, "pause");
    void OnNextThumb(object sender, EventArgs args) => _ = App.Session.RequestAsync(true, "playlist", "index", "+1");

    protected override void OnClosing(CancelEventArgs closingArgs)
    {
        if (App.IsQuitting) return;
        closingArgs.Cancel = true;
        if (!App.Settings.General.CloseHidesToTray) { App.Quit(); return; }
        Hide();
        if (!App.Settings.General.CloseToTrayTipShown)
        {
            App.ShowTip("WinSqueeze is still playing", "Closing the window hides it to the tray. Quit from the tray menu.");
            App.Settings.General.CloseToTrayTipShown = true; App.Settings.Save();
        }
    }
}
