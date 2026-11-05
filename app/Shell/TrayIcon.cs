using System.Drawing;
using System.Windows.Forms;
namespace WinSqueeze.Shell;

// Tray icon and menu (WinForms NotifyIcon runs fine on the WPF dispatcher)
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon notifyIcon;
    readonly ToolStripMenuItem outputMenu = new("Output device"), playerMenu = new("Player"), windowModeMenu = new("Window mode"), skinMenu = new("Skin");
    App App => App.Instance;

    public TrayIcon()
    {
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Play/Pause", null, async (sender, args) => await App.SendPlayerCommandAsync("pause"));
        trayMenu.Items.Add("Next", null, async (sender, args) => await App.SendPlayerCommandAsync("playlist", "index", "+1"));
        trayMenu.Items.Add("Previous", null, async (sender, args) => await App.SendPlayerCommandAsync("playlist", "index", "-1"));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("New playlist", null, (sender, args) => App.NewPlaylist());
        trayMenu.Items.Add(outputMenu);
        trayMenu.Items.Add(playerMenu);
        trayMenu.Items.Add(windowModeMenu);
        trayMenu.Items.Add(skinMenu);
        foreach (var (mode, label) in new[] { ("desktop", "Desktop"), ("vertical", "Vertical"), ("mini", "Mini") }) windowModeMenu.DropDownItems.Add(label, null, (sender, args) => App.SetWindowMode(mode));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Check for updates", null, async (sender, args) => { await App.Updater.CheckNowAsync(); if (App.Updater.ReadyVersion == "") ShowTip("WinSqueeze", App.Updater.StatusText); });
        trayMenu.Items.Add("Settings", null, (sender, args) => App.OpenSettings());
        trayMenu.Items.Add("Quit", null, (sender, args) => App.Quit());
        trayMenu.Opening += (sender, args) => { RebuildOutputMenu(); RebuildPlayerMenu(); RebuildSkinMenu(); };
        notifyIcon = new NotifyIcon { Icon = CreateIcon(), Text = "WinSqueeze", ContextMenuStrip = trayMenu, Visible = true };
        notifyIcon.MouseClick += (sender, args) => { if (args.Button == MouseButtons.Left) App.ToggleMainWindow(); };
    }

    void RebuildOutputMenu()
    {
        outputMenu.DropDownItems.Clear();
        var profile = App.Settings.ActiveProfile;
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
        if (outputMenu.DropDownItems.Count == 0) outputMenu.DropDownItems.Add(new ToolStripMenuItem("No devices found") { Enabled = false });
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
                var playerItem = new ToolStripMenuItem(Server.LyrionClient.ReadString(player, "name") + (playerId == App.Settings.PlayerMac ? " (this PC)" : "")) { Checked = playerId == selectedId, Enabled = Server.LyrionClient.ReadString(player, "connected") != "0" };
                playerItem.Click += async (sender, args) => await App.SelectPlayerAsync(playerId);
                playerMenu.DropDownItems.Add(playerItem);
            }
        }
        catch (System.Text.Json.JsonException) { }
        if (playerMenu.DropDownItems.Count == 0) playerMenu.DropDownItems.Add(new ToolStripMenuItem("No players found") { Enabled = false });
    }

    void RebuildSkinMenu()
    {
        skinMenu.DropDownItems.Clear();
        foreach (var (skin, label) in new[] { ("auto", "Follow Windows"), ("dark", "Frosted dark"), ("light", "Frosted light"), ("classic", "Squeezebox classic"), ("amber", "Hi-fi amber") })
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

    public void ShowTip(string title, string text) => notifyIcon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    static Icon CreateIcon()
    {
        using var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/WinSqueeze.ico")).Stream;
        return new Icon(iconStream, SystemInformation.SmallIconSize);
    }

    public void Dispose() { notifyIcon.Visible = false; notifyIcon.Dispose(); }
}
