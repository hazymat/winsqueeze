using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WinSqueeze.Settings;
using WinSqueeze.Shell;
namespace WinSqueeze;

// VU Builder (SPEC.md › VU Builder): design your own VU meter. Opened from Settings › Meters; looks like Settings (white cards on pale grey).
// Left: the meter pair on black, live with the music (a WebView2 page, #vu, drawn by the same code as the app's meters) and the starting points; right: every setting.
// Saved meters join the meter styles (ticked, so a click on the meters reaches them); Export and Import share one as a small file
public sealed class VuBuilderWindow : Window
{
    readonly WebView2 webView = new();
    readonly ComboBox meterPicker = new() { Width = 220, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox nameBox = new() { Width = 200, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 3, 4, 3) };
    readonly StackPanel settingsPanel = new() { Margin = new Thickness(0, 0, 16, 16) };
    readonly Button deleteButton = new() { Content = L.T("Delete") };
    readonly TextBlock savedNote = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)) };
    CustomMeter editing = CustomMeter.DarkRoom();
    string? editingId; // null: a new meter, not saved yet
    bool webViewReady, dirty, loading;
    public event Action? Saved;
    App App => App.Instance;
    static readonly JsonSerializerOptions camelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    static readonly Brush pageBrush = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA)), lineBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xE5, 0xE8)), quietBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));

    public VuBuilderWindow()
    {
        Title = L.T("VU Builder"); Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/WinSqueeze.ico"));
        Width = 1180; Height = 760; MinWidth = 900; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = pageBrush;
        editing.Name = ""; editing.Id = "";
        var root = new DockPanel();
        // Top bar: which meter, its name, and the file actions
        var topBar = new DockPanel { Margin = new Thickness(16, 12, 16, 12), LastChildFill = false };
        void AddTop(UIElement element, Dock dock = Dock.Left) { DockPanel.SetDock(element, dock); topBar.Children.Add(element); }
        AddTop(new TextBlock { Text = L.T("Meter"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }); AddTop(meterPicker);
        AddTop(new TextBlock { Text = L.T("Name"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 8, 0) }); AddTop(nameBox);
        var exportButton = new Button { Content = L.T("Export…") }; var importButton = new Button { Content = L.T("Import…") };
        foreach (var button in new[] { exportButton, importButton, deleteButton }) { button.Padding = new Thickness(12, 4, 12, 4); button.Margin = new Thickness(8, 0, 0, 0); AddTop(button, Dock.Right); }
        var topBorder = new Border { Background = Brushes.White, BorderBrush = lineBrush, BorderThickness = new Thickness(0, 0, 0, 1), Child = topBar };
        DockPanel.SetDock(topBorder, Dock.Top); root.Children.Add(topBorder);
        // Bottom bar: Save and Close
        var bottomBar = new DockPanel { Margin = new Thickness(16, 10, 16, 10), LastChildFill = false };
        var saveButton = new Button { Content = L.T("Save"), Padding = new Thickness(18, 5, 18, 5), IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
        var closeButton = new Button { Content = L.T("Close"), Padding = new Thickness(18, 5, 18, 5), IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(closeButton, Dock.Right); DockPanel.SetDock(saveButton, Dock.Right); bottomBar.Children.Add(closeButton); bottomBar.Children.Add(saveButton); bottomBar.Children.Add(savedNote);
        var bottomBorder = new Border { Background = Brushes.White, BorderBrush = lineBrush, BorderThickness = new Thickness(0, 1, 0, 0), Child = bottomBar };
        DockPanel.SetDock(bottomBorder, Dock.Bottom); root.Children.Add(bottomBorder);
        // Body: preview and starting points on the left, settings on the right
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        var left = new DockPanel { Margin = new Thickness(16) };
        var presetPanel = new WrapPanel();
        foreach (var preset in CustomMeter.Presets()) { var button = new Button { Content = preset.Name, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 8) }; button.Click += (sender, args) => StartFrom(preset); presetPanel.Children.Add(button); }
        var presetCard = Card(L.T("Start from"), presetPanel); presetCard.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(presetCard, Dock.Bottom); left.Children.Add(presetCard);
        left.Children.Add(new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = webView });
        body.Children.Add(left);
        var settingsScroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = settingsPanel, Margin = new Thickness(0, 16, 0, 0) };
        Grid.SetColumn(settingsScroller, 1); body.Children.Add(settingsScroller);
        root.Children.Add(body);
        Content = root;

        meterPicker.SelectionChanged += (sender, args) => { if (!loading && meterPicker.SelectedItem is ComboBoxItem { Tag: string pickedId }) Pick(pickedId); };
        nameBox.TextChanged += (sender, args) => { if (!loading) { editing.Name = nameBox.Text; Changed(false); } };
        saveButton.Click += (sender, args) => Save();
        closeButton.Click += (sender, args) => Close();
        deleteButton.Click += (sender, args) => Delete();
        exportButton.Click += (sender, args) => Export();
        importButton.Click += (sender, args) => Import();
        Closing += (sender, args) => { if (dirty && !AskToSave()) args.Cancel = true; };
        Loaded += async (sender, args) => await InitWebViewAsync();
        App.Visualiser.LevelsReady += OnLevels;
        Closed += (sender, args) => App.Visualiser.LevelsReady -= OnLevels;
        // Opens on the first meter you made, or a new one starting from the Dark room VU
        var first = App.Settings.Meters.Custom.FirstOrDefault();
        if (first != null) Pick(first.Id); else { FillPicker(); BuildSettings(); }
    }

    void OnLevels(string levelsJson) { if (webViewReady) Dispatcher.BeginInvoke(() => { if (IsVisible && WindowState != WindowState.Minimized) webView.CoreWebView2?.PostWebMessageAsJson(levelsJson); }); }

    async Task InitWebViewAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataFolder, "WebView2"));
        await webView.EnsureCoreWebView2Async(environment);
        webView.DefaultBackgroundColor = System.Drawing.Color.Black;
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping("app.winsqueeze", Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.DenyCors);
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
        webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
        webView.CoreWebView2.WebMessageReceived += (sender, args) =>
        {
            using var message = JsonDocument.Parse(args.WebMessageAsJson);
            if (message.RootElement.GetProperty("type").GetString() != "ready") return;
            webViewReady = true; PushDesign();
            webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "meterSettings", referenceDbfs = App.Settings.Meters.ReferenceDbfs, autoReference = App.Settings.Meters.AutoReference }));
        };
        webView.CoreWebView2.Navigate($"https://app.winsqueeze/index.html?lang={L.Code}#vu");
    }

    void PushDesign() { if (webViewReady) webView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "vuDesign", design = editing }, camelCase)); }
    void Changed(bool redraw = true) { dirty = true; savedNote.Text = ""; if (redraw) PushDesign(); }

    // ---- Which meter ----
    void FillPicker()
    {
        loading = true;
        meterPicker.Items.Clear();
        foreach (var meter in App.Settings.Meters.Custom) meterPicker.Items.Add(new ComboBoxItem { Content = meter.Name, Tag = meter.Id });
        meterPicker.Items.Add(new ComboBoxItem { Content = L.T("New meter"), Tag = "" });
        meterPicker.SelectedItem = meterPicker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == (editingId ?? ""));
        nameBox.Text = editing.Name; deleteButton.IsEnabled = editingId != null;
        loading = false;
    }

    void Pick(string pickedId)
    {
        if (pickedId == (editingId ?? "")) return;
        if (dirty && !AskToSave()) { FillPicker(); return; }
        var saved = App.Settings.Meters.Custom.FirstOrDefault(meter => meter.Id == pickedId);
        editing = saved?.Copy() ?? NewMeter(CustomMeter.DarkRoom()); editingId = saved?.Id;
        dirty = false; FillPicker(); BuildSettings(); PushDesign();
    }

    static CustomMeter NewMeter(CustomMeter from) { var meter = from.Copy(); meter.Id = Guid.NewGuid().ToString("N")[..8]; meter.Name = ""; return meter; }

    // A starting point replaces the look, and keeps the name you typed
    void StartFrom(CustomMeter preset)
    {
        var name = editing.Name; var keepId = editing.Id;
        editing = preset.Copy(); editing.Id = keepId; editing.Name = name;
        BuildSettings(); Changed();
    }

    // ---- Save, delete, files ----
    bool Save()
    {
        if (string.IsNullOrWhiteSpace(editing.Name)) { var count = App.Settings.Meters.Custom.Count + 1; editing.Name = L.T("My VU {count}", ("count", count)); }
        var meters = App.Settings.Meters;
        if (string.IsNullOrEmpty(editing.Id)) editing.Id = Guid.NewGuid().ToString("N")[..8];
        var index = meters.Custom.FindIndex(meter => meter.Id == editing.Id);
        if (index >= 0) meters.Custom[index] = editing.Copy(); else meters.Custom.Add(editing.Copy());
        meters.Normalise();
        if (editingId == null) { var style = meters.Styles.FirstOrDefault(choice => choice.Key == editing.StyleKey); if (style != null) style.On = true; } // a new meter joins the click cycle straight away
        editingId = editing.Id;
        App.Settings.Save(); App.ApplyMeters(); Saved?.Invoke();
        dirty = false; FillPicker();
        savedNote.Text = L.T("Saved. Click the meters to reach it, or change the order in Settings › Meters.");
        return true;
    }

    // Unsaved changes: Yes saves, No drops them, Cancel stays
    bool AskToSave()
    {
        var choice = MessageBox.Show(this, L.T("Save the changes to this meter?"), L.T("VU Builder"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel) return false;
        if (choice == MessageBoxResult.Yes) return Save();
        dirty = false; return true;
    }

    void Delete()
    {
        if (editingId == null) return;
        if (MessageBox.Show(this, L.T("Delete {name}?", ("name", editing.Name)), L.T("VU Builder"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        App.Settings.Meters.Custom.RemoveAll(meter => meter.Id == editingId);
        App.Settings.Meters.Normalise(); App.Settings.Save(); App.ApplyMeters(); Saved?.Invoke();
        editing = NewMeter(CustomMeter.DarkRoom()); editingId = null; dirty = false;
        FillPicker(); BuildSettings(); PushDesign();
    }

    void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = L.T("VU meter") + " (*.vu.json)|*.vu.json", FileName = (string.IsNullOrWhiteSpace(editing.Name) ? "VU" : string.Concat(editing.Name.Split(Path.GetInvalidFileNameChars()))) + ".vu.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(editing, camelCase)); }
        catch (Exception error) { MessageBox.Show(this, error.Message, L.T("VU Builder"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    // An imported meter opens as a new one; Save keeps it
    void Import()
    {
        if (dirty && !AskToSave()) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = L.T("VU meter") + " (*.vu.json)|*.vu.json|JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = JsonSerializer.Deserialize<CustomMeter>(File.ReadAllText(dialog.FileName), camelCase) ?? throw new InvalidDataException();
            editing = NewMeter(imported); editing.Name = imported.Name; editingId = null;
            FillPicker(); BuildSettings(); Changed();
        }
        catch (Exception) { MessageBox.Show(this, L.T("That file isn't a VU meter."), L.T("VU Builder"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    // ---- Settings cards ----
    Border Card(string title, UIElement content)
    {
        var stack = new StackPanel(); stack.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) }); stack.Children.Add(content);
        return new Border { Background = Brushes.White, BorderBrush = lineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 10), Margin = new Thickness(0, 0, 0, 12), Child = stack };
    }

    static Grid Row(string label, UIElement control)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
        row.Children.Add(text); Grid.SetColumn(control, 1); row.Children.Add(control);
        return row;
    }

    UIElement Choice(string label, (string key, string name)[] options, Func<string> read, Action<string> write)
    {
        var box = new ComboBox { ItemsSource = options.Select(option => new ComboBoxItem { Content = option.name, Tag = option.key }).ToList() };
        box.SelectedItem = ((List<ComboBoxItem>)box.ItemsSource).FirstOrDefault(item => (string)item.Tag == read()) ?? ((List<ComboBoxItem>)box.ItemsSource)[0];
        box.SelectionChanged += (sender, args) => { if (box.SelectedItem is ComboBoxItem { Tag: string key }) { write(key); Changed(); } };
        return Row(label, box);
    }

    UIElement Number(string label, double minimum, double maximum, Func<double> read, Action<double> write, string lowEnd = "", string highEnd = "")
    {
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = read(), VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (sender, args) => { write(Math.Round(slider.Value, 2)); Changed(); };
        if (lowEnd == "") return Row(label, slider);
        var ends = new DockPanel(); var high = new TextBlock { Text = highEnd, Foreground = quietBrush, FontSize = 11 }; DockPanel.SetDock(high, Dock.Right); ends.Children.Add(high); ends.Children.Add(new TextBlock { Text = lowEnd, Foreground = quietBrush, FontSize = 11 });
        var stack = new StackPanel(); stack.Children.Add(slider); stack.Children.Add(ends);
        return Row(label, stack);
    }

    // Colour: a few swatches that suit meters, More… for any colour
    UIElement Colour(string label, string[] swatches, Func<string> read, Action<string> write)
    {
        var panel = new WrapPanel();
        var swatchBorders = new List<Border>();
        void Mark() { foreach (var swatch in swatchBorders) swatch.BorderBrush = string.Equals((string)swatch.Tag, read(), StringComparison.OrdinalIgnoreCase) ? new SolidColorBrush(Color.FromRgb(0x2F, 0x7F, 0xD8)) : lineBrush; }
        void Set(string colour) { write(colour.ToLowerInvariant()); Mark(); Changed(); }
        foreach (var colour in swatches.Contains(read(), StringComparer.OrdinalIgnoreCase) ? swatches : swatches.Append(read()))
        {
            var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 6, 4), BorderThickness = new Thickness(2), Tag = colour, Cursor = System.Windows.Input.Cursors.Hand, Background = new SolidColorBrush(ParseColour(colour)), ToolTip = colour };
            swatch.MouseLeftButtonUp += (sender, args) => Set((string)swatch.Tag);
            swatchBorders.Add(swatch); panel.Children.Add(swatch);
        }
        var more = new Button { Content = L.T("More…"), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(2, 0, 0, 4), VerticalAlignment = VerticalAlignment.Center };
        more.Click += (sender, args) =>
        {
            var current = ParseColour(read());
            using var dialog = new System.Windows.Forms.ColorDialog { Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B), FullOpen = true };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            Set($"#{dialog.Color.R:x2}{dialog.Color.G:x2}{dialog.Color.B:x2}"); BuildSettings();
        };
        panel.Children.Add(more); Mark();
        return Row(label, panel);
    }

    // A colour from a design (an imported file may hold anything): black when it can't be read
    static Color ParseColour(string colour) { try { return (Color)ColorConverter.ConvertFromString(colour); } catch (Exception) { return Colors.Black; } }

    UIElement Words(string label, Func<string> read, Action<string> write)
    {
        var box = new TextBox { Text = read(), Padding = new Thickness(4, 2, 4, 2), MaxLength = 24 };
        box.TextChanged += (sender, args) => { write(box.Text); Changed(); };
        return Row(label, box);
    }

    UIElement Tick(string label, Func<bool> read, Action<bool> write)
    {
        var box = new CheckBox { Content = label, IsChecked = read(), Margin = new Thickness(110, 4, 0, 4) };
        box.Click += (sender, args) => { write(box.IsChecked == true); Changed(); };
        return box;
    }

    void BuildSettings()
    {
        var meter = editing;
        settingsPanel.Children.Clear();
        StackPanel Stack(params UIElement[] rows) { var stack = new StackPanel(); foreach (var row in rows) stack.Children.Add(row); return stack; }
        settingsPanel.Children.Add(Card(L.T("Text"), Stack(
            Choice(L.T("Font"), new[] { ("modern", L.T("Modern")), ("engraved", L.T("Engraved")), ("deco", L.T("Art deco")), ("thirties", L.T("Thirties")), ("hifi", L.T("70s hi-fi")), ("typewriter", L.T("Typewriter")), ("condensed", L.T("Condensed")), ("terminal", L.T("Terminal")) }, () => meter.Font, value => meter.Font = value),
            Number(L.T("Size"), 0.6, 1.6, () => meter.FontSize, value => meter.FontSize = value),
            Choice(L.T("Bottom label"), new[] { ("vu", "VU"), ("channel", L.T("L or R")), ("reference", L.T("0 VU level")), ("none", L.T("None")) }, () => meter.Label, value => meter.Label = value),
            Words(L.T("Name on the face"), () => meter.Brand, value => meter.Brand = value))));
        settingsPanel.Children.Add(Card(L.T("Scale"), Stack(
            Number(L.T("Shape"), 0, 1, () => meter.Roundness, value => meter.Roundness = value, L.T("Flat"), L.T("Round")),
            Choice(L.T("Arc"), new[] { ("line", L.T("Line")), ("band", L.T("Line and red band")), ("double", L.T("Double line")), ("wide", L.T("Wide band")), ("dotted", L.T("Dotted")), ("segments", L.T("Segments")), ("ticks", L.T("Marks only")) }, () => meter.Arc, value => meter.Arc = value),
            Number(L.T("Line weight"), 0.5, 3, () => meter.Weight, value => meter.Weight = value, L.T("Thin"), L.T("Bold")),
            Colour(L.T("Below 0"), new[] { "#3fa57a", "#e9dcc0", "#46e6ff", "#7dffb0", "#ffffff", "#1d1a14" }, () => meter.Cold, value => meter.Cold = value),
            Colour(L.T("Above 0"), new[] { "#b5402f", "#c4291c", "#ff6a3a", "#ffd54a", "#ff4d6d" }, () => meter.Hot, value => meter.Hot = value))));
        settingsPanel.Children.Add(Card(L.T("Needle"), Stack(
            Choice(L.T("Shape"), new[] { ("blade", L.T("Blade")), ("line", L.T("Line")), ("bar", L.T("Bar")), ("arrow", L.T("Fine, arrow tip")), ("lance", L.T("Fine, lance tip")), ("spade", L.T("Fine, spade tip")), ("flag", L.T("Fine, with a flag")), ("ball", L.T("Fine, ball tip")) }, () => meter.NeedleShape, value => meter.NeedleShape = value),
            Choice(L.T("Finish"), new[] { ("flat", L.T("Flat colour")), ("brass", L.T("Polished brass")), ("chrome", L.T("Chrome")), ("black", L.T("Black lacquer")) }, () => meter.NeedleFinish, value => meter.NeedleFinish = value),
            Number(L.T("Shadow"), 0, 1, () => meter.Shadow, value => meter.Shadow = value, L.T("None"), L.T("Strong")),
            Choice(L.T("Comes from"), new[] { ("bottom", L.T("Bottom")), ("top", L.T("Top")) }, () => meter.NeedleFrom, value => meter.NeedleFrom = value),
            Choice(L.T("Base"), new[] { ("none", L.T("None")), ("ring", L.T("Ring")), ("filled", L.T("Filled")), ("dome", L.T("Dome")), ("screw", L.T("Dome with screw")), ("knurled", L.T("Knurled chrome")), ("brass", L.T("Brass cap")) }, () => meter.Base, value => meter.Base = value),
            Colour(L.T("Colour"), new[] { "#ecd9a8", "#ffffff", "#ff6a3a", "#e23b2e", "#111111", "#d8f8ff" }, () => meter.NeedleColour, value => meter.NeedleColour = value))));
        settingsPanel.Children.Add(Card(L.T("Face and light"), Stack(
            Colour(L.T("Back"), new[] { "#000000", "#16181a", "#0b1626", "#efe2c4", "#f6f0e2", "#ffffff" }, () => meter.Back, value => meter.Back = value),
            Choice(L.T("Bulb"), new[] { ("none", L.T("Off")), ("tungsten", L.T("Tungsten")), ("warm", L.T("Warm white")), ("cool", L.T("Cool white")), ("amber", L.T("Amber")), ("green", L.T("Green")), ("blue", L.T("Blue")) }, () => meter.Lamp, value => meter.Lamp = value),
            Choice(L.T("Lit from"), new[] { ("below", L.T("Below")), ("above", L.T("Above")), ("left", L.T("Left")), ("right", L.T("Right")), ("behind", L.T("Behind the face")) }, () => meter.LampFrom, value => meter.LampFrom = value),
            Choice(L.T("Display"), new[] { ("analogue", L.T("Analogue")), ("oled", "OLED") }, () => meter.Display == "dots" ? "oled" : meter.Display, value => meter.Display = value),
            Tick(L.T("Glow"), () => meter.Glow, value => meter.Glow = value))));
        settingsPanel.Children.Add(Card(L.T("Case"), Stack(
            Choice(L.T("Casing"), new[] { ("none", L.T("None")), ("line", L.T("Line")), ("bezel", L.T("Bezel")), ("metal", L.T("Brushed metal")), ("wood", L.T("Wood")), ("acrylic", L.T("Clear acrylic")) }, () => meter.Casing, value => meter.Casing = value),
            Choice(L.T("Glass"), new[] { ("none", L.T("None")), ("clear", L.T("Clear")), ("smoked", L.T("Smoked")) }, () => meter.Glass, value => meter.Glass = value))));
    }
}
