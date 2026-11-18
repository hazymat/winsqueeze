using System.Drawing;
using System.Windows.Forms;
using WinSqueeze.Audio;
namespace WinSqueeze.Shell;

// Tray icon and menu (WinForms NotifyIcon runs fine on the WPF dispatcher)
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon notifyIcon;
    readonly ToolStripMenuItem updateItem = new() { Visible = false }; // "Update WinSqueeze to x" with the blue dot, top of the menu while an update is ready; flashes while the menu is open
    readonly System.Windows.Forms.Timer flashTimer = new() { Interval = 900 }; // update ready: the tray dot flashes until seen (card or menu), the menu row whenever the menu is open
    bool flashOn = true, menuOpen;
    string seenUpdate = ""; // the ready version the user has seen explained (then the dot stays solid blue)
    string tipText = "WinSqueeze", iconKey = "";
    readonly Image blueDot = Dot(NoticeCard.UpdateColour), noDot = new Bitmap(64, 64);
    string ReadyVersion => App.Updater.ReadyVersion;
    bool DotShown => ReadyVersion != "" && App.Settings.General.UpdateDot; // Settings › General › Show a blue dot on the tray icon when an update is ready
    readonly ToolStripMenuItem outputMenu = new(L.T("Output device")), playerMenu = new(L.T("Player")), sleepMenu = new(L.T("Sleep")), windowModeMenu = new(L.T("Window mode")), skinMenu = new(L.T("Skin"));
    App App => App.Instance;

    public TrayIcon()
    {
        var trayMenu = new ContextMenuStrip();
        var versionRow = new ToolStripLabel { BackColor = SystemColors.ControlLight, ForeColor = SystemColors.GrayText, Font = new Font(SystemFonts.MenuFont!.FontFamily, SystemFonts.MenuFont.Size - 1), TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(0, 2, 0, 2) }; // shaded, non-clickable version row
        trayMenu.Items.Add(versionRow);
        updateItem.Image = blueDot; updateItem.Click += async (sender, args) => await App.Updater.RestartToUpdateAsync();
        trayMenu.Items.Add(updateItem);
        trayMenu.Items.Add(L.T("Play/Pause"), null, async (sender, args) => await App.SendPlayerCommandAsync("pause"));
        trayMenu.Items.Add(L.T("Next"), null, async (sender, args) => await App.SendPlayerCommandAsync("playlist", "index", "+1"));
        trayMenu.Items.Add(L.T("Previous"), null, async (sender, args) => await App.SendPlayerCommandAsync("playlist", "index", "-1"));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(L.T("New playlist"), null, (sender, args) => App.NewPlaylist());
        trayMenu.Items.Add(outputMenu);
        trayMenu.Items.Add(playerMenu);
        trayMenu.Items.Add(sleepMenu);
        trayMenu.Items.Add(windowModeMenu);
        trayMenu.Items.Add(skinMenu);
        foreach (var (mode, label) in new[] { ("desktop", L.T("Desktop")), ("vertical", L.T("Vertical")), ("mini", L.T("Mini")) }) windowModeMenu.DropDownItems.Add(label, null, (sender, args) => App.SetWindowMode(mode));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(L.T("Check for updates"), null, async (sender, args) => { await App.Updater.CheckNowAsync(); if (App.Updater.ReadyVersion == "") ShowTip("WinSqueeze", App.Updater.StatusText); });
        trayMenu.Items.Add(L.T("Settings"), null, (sender, args) => App.OpenSettings());
        trayMenu.Items.Add(L.T("Quit"), null, (sender, args) => App.Quit());
        trayMenu.Opening += (sender, args) => { versionRow.Text = App.Updater.CurrentVersion; updateItem.Visible = ReadyVersion != ""; updateItem.Text = L.T("Update WinSqueeze to {version}", ("version", ReadyVersion)); updateItem.Image = blueDot; RebuildOutputMenu(); RebuildPlayerMenu(); RebuildSleepMenu(); RebuildSkinMenu(); };
        trayMenu.Opened += (sender, args) => { menuOpen = true; if (updateItem.Visible) updateItem.Font = new Font(trayMenu.Font, FontStyle.Bold); RefreshDot(); };
        trayMenu.Closed += (sender, args) => { menuOpen = false; if (updateItem.Visible) seenUpdate = ReadyVersion; RefreshDot(); }; // seen in the menu: the tray dot stops flashing
        notifyIcon = new NotifyIcon { Text = "WinSqueeze", ContextMenuStrip = trayMenu, Visible = App.Settings.General.ShowTrayIcon };
        // Left-click opens or hides the window as always; with the blue dot showing it also brings up the update card, so the dot always explains itself
        notifyIcon.MouseClick += (sender, args) => { if (args.Button != MouseButtons.Left) return; App.ToggleMainWindow(); if (DotShown) ShowUpdateCard(); };
        flashTimer.Tick += (sender, args) => { flashOn = !flashOn; flashTimer.Interval = flashOn ? 900 : 500; if (menuOpen && updateItem.Visible) { updateItem.Image = flashOn ? blueDot : noDot; updateItem.Invalidate(); } RefreshDot(); };
        App.Updater.Changed += () => App.Dispatcher.BeginInvoke(RefreshDot); // no pop-up when an update arrives (Mat 18 Nov): only the window's banner, Settings and this dot
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (sender, args) => App.Dispatcher.BeginInvoke(RefreshDot); // taskbar scaling
        RefreshDot();
    }

    // Only on a left-click while the dot shows, never by itself. Never says "Restart": it updates
    void ShowUpdateCard()
    {
        seenUpdate = ReadyVersion;
        NoticeCard.Show(L.T("Update ready"), L.T("WinSqueeze {version} is downloaded. The blue dot stays until you update.", ("version", ReadyVersion)), L.T("Update WinSqueeze now"), () => _ = App.Updater.RestartToUpdateAsync(), NoticeCard.UpdateColour, true);
        RefreshDot();
    }

    // The dot and the hover text: a slow blue flash until the update is seen, then solid blue until it's installed
    public void RefreshDot()
    {
        var unseen = DotShown && seenUpdate != ReadyVersion;
        var flashing = unseen || menuOpen && updateItem.Visible;
        if (flashing != flashTimer.Enabled) { flashOn = true; if (flashing) flashTimer.Start(); else flashTimer.Stop(); }
        var dot = DotShown && (!unseen || flashOn) ? NoticeCard.UpdateColour : "";
        var size = TrayIconSize(); var key = $"{dot}|{size}";
        if (key != iconKey) { iconKey = key; var oldIcon = notifyIcon.Icon; notifyIcon.Icon = CreateIcon(size, dot); if (oldIcon != null) { DestroyIcon(oldIcon.Handle); oldIcon.Dispose(); } }
        ApplyTip();
    }

    void RebuildOutputMenu()
    {
        outputMenu.DropDownItems.Clear();
        var profile = App.Settings.ActiveProfile;
        var defaultChoice = OutputDevice.WindowsDefault(App.WindowsDefaultName);
        var defaultItem = new ToolStripMenuItem(defaultChoice.DisplayName) { Checked = profile != null && profile.DeviceName == "" };
        defaultItem.Click += async (sender, args) => await App.SelectDeviceAsync(defaultChoice);
        outputMenu.DropDownItems.Add(defaultItem);
        foreach (var driverGroup in App.Devices.GroupBy(device => device.Driver))
        {
            outputMenu.DropDownItems.Add(new ToolStripMenuItem(driverGroup.Key) { Enabled = false });
            foreach (var device in driverGroup)
            {
                var deviceItem = new ToolStripMenuItem("   " + device.DisplayName) { Checked = profile != null && device.Matches(profile.DeviceName, profile.DeviceHostApi) };
                deviceItem.Click += async (sender, args) => await App.SelectDeviceAsync(device);
                outputMenu.DropDownItems.Add(deviceItem);
            }
        }
        if (App.Devices.Count == 0) outputMenu.DropDownItems.Add(new ToolStripMenuItem(L.T("No devices found")) { Enabled = false });
    }

    void RebuildPlayerMenu()
    {
        playerMenu.DropDownItems.Clear();
        var selectedId = App.Session.SelectedPlayerId == "" ? App.Settings.PlayerMac : App.Session.SelectedPlayerId;
        try
        {
            using var playersDocument = System.Text.Json.JsonDocument.Parse(App.Session.LastPlayersJson);
            foreach (var player in playersDocument.RootElement.EnumerateArray())
            {
                var playerId = Server.LyrionClient.ReadString(player, "playerid");
                var playerItem = new ToolStripMenuItem(Server.LyrionClient.ReadString(player, "name") + (playerId == App.Settings.PlayerMac ? " " + L.T("(this PC)") : "")) { Checked = playerId == selectedId, Enabled = Server.LyrionClient.ReadString(player, "connected") != "0" };
                playerItem.Click += async (sender, args) => await App.SelectPlayerAsync(playerId);
                playerMenu.DropDownItems.Add(playerItem);
            }
        }
        catch (System.Text.Json.JsonException) { }
        if (playerMenu.DropDownItems.Count == 0) playerMenu.DropDownItems.Add(new ToolStripMenuItem(L.T("No players found")) { Enabled = false });
    }

    // Lyrion's sleep timer for the selected player: stops it after the time, or when this track ends
    void RebuildSleepMenu()
    {
        sleepMenu.DropDownItems.Clear();
        var status = App.Session.LastStatus;
        var sleepLeft = Server.LyrionClient.ReadDouble(status, "will_sleep_in"); var trackLeft = Math.Ceiling(Server.LyrionClient.ReadDouble(status, "duration") - Server.LyrionClient.ReadDouble(status, "time"));
        sleepMenu.Visible = !App.IsOffline;
        sleepMenu.Text = sleepLeft > 0 ? L.T("Sleep ({count} min left)", ("count", Math.Ceiling(sleepLeft / 60))) : L.T("Sleep");
        foreach (var minutes in new[] { 15, 30, 45, 60, 90 }) sleepMenu.DropDownItems.Add(L.T("{count} minutes", ("count", minutes)), null, async (sender, args) => await App.Session.RequestAsync(true, "sleep", minutes * 60));
        if (trackLeft > 0) sleepMenu.DropDownItems.Add(L.T("End of this track"), null, async (sender, args) => await App.Session.RequestAsync(true, "sleep", (int)trackLeft));
        sleepMenu.DropDownItems.Add(new ToolStripMenuItem(L.T("Off"), null, async (sender, args) => await App.Session.RequestAsync(true, "sleep", 0)) { Checked = sleepLeft <= 0 });
    }

    void RebuildSkinMenu()
    {
        skinMenu.DropDownItems.Clear();
        foreach (var (skin, label) in new[] { ("auto", L.T("Follow Windows")), ("dark", L.T("Frosted dark")), ("light", L.T("Frosted light")), ("classic", L.T("Squeezebox classic")), ("amber", L.T("Hi-fi amber")) })
            skinMenu.DropDownItems.Add(new ToolStripMenuItem(label, null, (sender, args) => App.SetSkin(skin)) { Checked = App.Settings.General.Skin == skin });
    }

    public void Refresh()
    {
        var player = App.Player;
        tipText = player.State == Audio.PlayerState.Running && player.Device != null ? $"WinSqueeze · {player.Device.Name}{(player.OutputSampleRate > 0 ? $" · {player.OutputSampleRate / 1000.0:0.#} kHz" : "")}" : $"WinSqueeze · {player.StatusText}";
        ApplyTip();
    }

    // Tooltip: current track and artist
    public void RefreshNowPlaying(System.Text.Json.JsonElement status)
    {
        if (status.ValueKind != System.Text.Json.JsonValueKind.Object) return;
        var currentTrack = MediaControls.CurrentTrack(status);
        if (currentTrack == null) { Refresh(); return; }
        tipText = $"{Server.LyrionClient.ReadString(currentTrack.Value, "title")} · {Server.LyrionClient.ReadString(currentTrack.Value, "artist")}";
        ApplyTip();
    }

    // An update ready comes first, with its dot, so hovering always says what the dot is; Windows caps the tip at 127 characters
    void ApplyTip() { var text = (ReadyVersion != "" ? "🔵 " + L.T("Update ready: {version}", ("version", ReadyVersion)) + "\n" : "") + tipText; notifyIcon.Text = text.Length > 127 ? text[..127] : text; }

    public bool Visible { set => notifyIcon.Visible = value; }
    public void ShowTip(string title, string text) { if (notifyIcon.Visible) notifyIcon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info); } // no icon, no balloon

    // The app icon at the taskbar's size, with the dot bottom right and a clear ring cut round it so it stands apart from the bars
    static Icon CreateIcon(int size, string dotColour)
    {
        using var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/WinSqueeze.ico")).Stream;
        using var appIcon = new Icon(iconStream, size, size);
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; graphics.DrawIcon(appIcon, new Rectangle(0, 0, size, size));
            if (dotColour != "")
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float radius = size * 0.22f, ring = Math.Max(1f, size * 0.07f), centre = size - radius;
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                using (var clear = new SolidBrush(Color.Transparent)) graphics.FillEllipse(clear, centre - radius - ring, centre - radius - ring, (radius + ring) * 2, (radius + ring) * 2);
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                using var dotBrush = new SolidBrush(ColorTranslator.FromHtml(dotColour)); graphics.FillEllipse(dotBrush, centre - radius, centre - radius, radius * 2, radius * 2);
            }
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    // The size the taskbar shows tray icons at, from the taskbar's own scaling (SmallIconSize is fixed at sign-in, so after a dock change the icon came out fuzzy)
    static int TrayIconSize()
    {
        try { var taskbar = FindWindow("Shell_TrayWnd", null); var dpi = taskbar == IntPtr.Zero ? 0u : GetDpiForWindow(taskbar); if (dpi > 0) return GetSystemMetricsForDpi(49 /* SM_CXSMICON */, dpi); } catch { }
        return SystemInformation.SmallIconSize.Width;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr FindWindow(string className, string? windowName);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);

    static Image Dot(string colour)
    {
        var bitmap = new Bitmap(64, 64); // large and scaled down by the menu, so it stays sharp at any scaling
        using var graphics = Graphics.FromImage(bitmap); graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(ColorTranslator.FromHtml(colour)); graphics.FillEllipse(brush, 8, 8, 48, 48);
        return bitmap;
    }

    public void Dispose() { notifyIcon.Visible = false; notifyIcon.Dispose(); }
}
