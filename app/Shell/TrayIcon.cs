using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace WinSqueeze.Shell;

// Tray icon and menu (WinForms NotifyIcon runs fine on the WPF dispatcher)
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon notifyIcon;
    readonly ToolStripMenuItem outputMenu = new("Output device");
    App App => App.Instance;

    public TrayIcon()
    {
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Play/Pause", null, async (sender, args) => await App.SendPlayerCommandAsync("pause"));
        trayMenu.Items.Add("Next", null, async (sender, args) => await App.SendPlayerCommandAsync("playlist", "index", "+1"));
        trayMenu.Items.Add("Previous", null, async (sender, args) => await App.SendPlayerCommandAsync("playlist", "index", "-1"));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(new ToolStripMenuItem("New playlist (Phase 4)") { Enabled = false });
        trayMenu.Items.Add(outputMenu);
        trayMenu.Items.Add(new ToolStripMenuItem("Player (Phase 4)") { Enabled = false });
        trayMenu.Items.Add(new ToolStripMenuItem("Window mode (Phase 5)") { Enabled = false });
        trayMenu.Items.Add(new ToolStripMenuItem("Skin (Phase 5)") { Enabled = false });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Check for updates", null, async (sender, args) => { await App.Updater.CheckNowAsync(); if (App.Updater.ReadyVersion == "") ShowTip("WinSqueeze", App.Updater.StatusText); });
        trayMenu.Items.Add("Settings", null, (sender, args) => App.OpenSettings());
        trayMenu.Items.Add("Quit", null, (sender, args) => App.Quit());
        trayMenu.Opening += (sender, args) => RebuildOutputMenu();
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

    public void Refresh()
    {
        var player = App.Player;
        var tooltipText = player.State == Audio.PlayerState.Running && player.Device != null ? $"WinSqueeze · {player.Device.Name}{(player.OutputSampleRate > 0 ? $" · {player.OutputSampleRate / 1000.0:0.#} kHz" : "")}" : $"WinSqueeze · {player.StatusText}";
        notifyIcon.Text = tooltipText.Length > 127 ? tooltipText[..127] : tooltipText;
    }

    public void ShowTip(string title, string text) => notifyIcon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    // Placeholder icon drawn at runtime until the real one lands in Phase 5
    static Icon CreateIcon()
    {
        using var iconBitmap = new Bitmap(32, 32);
        using (var canvas = Graphics.FromImage(iconBitmap))
        {
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            canvas.Clear(Color.Transparent);
            using var discBrush = new SolidBrush(Color.FromArgb(79, 195, 247));
            canvas.FillEllipse(discBrush, 1, 1, 30, 30);
            using var letterFont = new Font("Segoe UI", 16, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var letterFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            canvas.DrawString("W", letterFont, Brushes.Black, new RectangleF(0, 1, 32, 32), letterFormat);
        }
        return Icon.FromHandle(iconBitmap.GetHicon());
    }

    public void Dispose() { notifyIcon.Visible = false; notifyIcon.Dispose(); }
}
