using System.Drawing;
using System.Windows.Forms;
using WinSqueeze.Audio;
namespace WinSqueeze.Shell;

// Tray icon and menu (WinForms NotifyIcon runs fine on the WPF dispatcher)
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon notifyIcon;
    readonly ToolStripMenuItem outputMenu = new(L.T("Output device")), playerMenu = new(L.T("Player")), sleepMenu = new(L.T("Sleep")), windowModeMenu = new(L.T("Window mode")), skinMenu = new(L.T("Skin"));
    App App => App.Instance;

    public TrayIcon()
    {
        var trayMenu = new ContextMenuStrip();
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
        trayMenu.Opening += (sender, args) => { RebuildOutputMenu(); RebuildPlayerMenu(); RebuildSleepMenu(); RebuildSkinMenu(); };
        notifyIcon = new NotifyIcon { Icon = CreateIcon(), Text = "WinSqueeze", ContextMenuStrip = trayMenu, Visible = App.Settings.General.ShowTrayIcon };
        notifyIcon.MouseClick += (sender, args) => { if (args.Button == MouseButtons.Left) App.ToggleMainWindow(); };
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
        var tooltipText = player.State == Audio.PlayerState.Running && player.Device != null ? $"WinSqueeze · {player.Device.Name}{(player.OutputSampleRate > 0 ? $" · {player.OutputSampleRate / 1000.0:0.#} kHz" : "")}" : $"WinSqueeze · {player.StatusText}";
        notifyIcon.Text = tooltipText.Length > 127 ? tooltipText[..127] : tooltipText;
    }

    // Tooltip: current track and artist
    public void RefreshNowPlaying(System.Text.Json.JsonElement status)
    {
        if (status.ValueKind != System.Text.Json.JsonValueKind.Object) return;
        var currentTrack = MediaControls.CurrentTrack(status);
        if (currentTrack == null) { Refresh(); return; }
        var tooltipText = $"{Server.LyrionClient.ReadString(currentTrack.Value, "title")} · {Server.LyrionClient.ReadString(currentTrack.Value, "artist")}";
        notifyIcon.Text = tooltipText.Length > 127 ? tooltipText[..127] : tooltipText;
    }

    public bool Visible { set => notifyIcon.Visible = value; }
    public void ShowTip(string title, string text) { if (notifyIcon.Visible) notifyIcon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info); } // no icon, no balloon

    static Icon CreateIcon()
    {
        using var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/WinSqueeze.ico")).Stream;
        return new Icon(iconStream, SystemInformation.SmallIconSize);
    }

    public void Dispose() { notifyIcon.Visible = false; notifyIcon.Dispose(); }
}
