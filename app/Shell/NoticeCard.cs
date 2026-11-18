using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
namespace WinSqueeze.Shell;

// WinSqueeze's own small glass card above the clock, never a Windows notification (same card as WinSwitch and WinGCal).
// The tray dot, its menu row and the card share one colour, so a coloured dot always leads to a card of the same colour: blue = update ready, amber = a message (none yet)
public sealed class NoticeCard : Window
{
    public const string UpdateColour = "#4CC2FF", MessageColour = "#E0912F";
    static NoticeCard? showing;
    readonly DispatcherTimer fadeTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    // clicked: asked for by a click on the tray icon, so it shows even in full screen or Do not disturb (a click never does nothing)
    public static void Show(string title, string text, string actionLabel, Action action, string accent, bool clicked)
    {
        if (!clicked && IsQuiet()) return;
        showing?.Close();
        showing = new NoticeCard(title, text, actionLabel, action, accent);
        showing.Closed += (sender, args) => { if (sender == showing) showing = null; };
        showing.ShowAtCorner();
    }

    NoticeCard(string title, string text, string actionLabel, Action action, string accent)
    {
        var accentColour = (Color)ColorConverter.ConvertFromString(accent); var accentBrush = new SolidColorBrush(accentColour);
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        Width = 372; SizeToContent = SizeToContent.Height; FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        var light = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF7)); var quiet = new SolidColorBrush(Color.FromRgb(0xB8, 0xC0, 0xCC));
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6), Children = { accent == UpdateColour ? UpdateLed() : new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = accentBrush, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "WinSqueeze", Foreground = quiet, FontSize = 11 } } };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, Foreground = light, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (text != "") body.Children.Add(new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD0, 0xDA)), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        var line = new DockPanel();
        var link = new TextBlock { Text = actionLabel, Foreground = new SolidColorBrush(Color.FromRgb(0x8C, 0xC0, 0xFF)), FontWeight = FontWeights.SemiBold, FontSize = 12.5, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        link.MouseLeftButtonUp += (sender, args) => { action(); Close(); };
        DockPanel.SetDock(link, Dock.Right); line.Children.Add(link); line.Children.Add(body);
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x20, 0x24, 0x2C)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, accentColour.R, accentColour.G, accentColour.B)), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(10), Child = new StackPanel { Children = { header, line } }, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.4 } };
        fadeTimer.Tick += (sender, args) => { if (!IsMouseOver) { fadeTimer.Stop(); FadeAndClose(); } }; // fades after 5 s, unless the pointer is on it
    }

    // The update LED: the hi-fi standby lamp's blue pulse (HiFi.svelte .standby.update, 1.6 s), glass lens lit from top left, glow and brightness breathing together. The tray dot only approximates it
    static FrameworkElement UpdateLed()
    {
        var lens = new RadialGradientBrush { GradientOrigin = new Point(0.4, 0.35), Center = new Point(0.4, 0.35), RadiusX = 0.65, RadiusY = 0.65, GradientStops = { new GradientStop(Color.FromRgb(0xD9, 0xEE, 0xFF), 0), new GradientStop(Color.FromRgb(0x2F, 0x8C, 0xFF), 0.45), new GradientStop(Color.FromRgb(0x0B, 0x3A, 0x8C), 1) } };
        var glow = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x3C, 0x96, 0xFF), ShadowDepth = 0, BlurRadius = 4, Opacity = 0.35 };
        var lamp = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = lens, Effect = glow, Opacity = 0.8 };
        var shine = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = Brushes.White, Opacity = 0 }; // brightness above 1
        var pulse = new CubicEase { EasingMode = EasingMode.EaseInOut }; var half = TimeSpan.FromSeconds(0.8);
        DoubleAnimation Breathe(double from, double to) => new(from, to, half) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = pulse };
        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, Breathe(4, 14));
        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, Breathe(0.35, 0.85));
        lamp.BeginAnimation(OpacityProperty, Breathe(0.8, 1));
        shine.BeginAnimation(OpacityProperty, Breathe(0, 0.22));
        return new Grid { Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, Children = { lamp, shine } };
    }

    // Bottom right of the main screen's work area, slides up
    void ShowAtCorner()
    {
        Opacity = 0; Show();
        var corner = BottomRight();
        Left = corner.X - Width - 6;
        var finalTop = corner.Y - ActualHeight - 6;
        Top = finalTop + 18;
        BeginAnimation(TopProperty, new DoubleAnimation(finalTop, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        fadeTimer.Start();
    }

    void FadeAndClose() { var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)); fade.Completed += (sender, args) => Close(); BeginAnimation(OpacityProperty, fade); }

    // Worked out from real pixels: WPF's WorkArea uses one scaling for every screen, so the card moves onto the main screen first (taking its scaling), then reads the work area there
    Point BottomRight()
    {
        var area = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, (int)SystemParameters.WorkArea.Width, (int)SystemParameters.WorkArea.Height);
        var handle = new WindowInteropHelper(this).EnsureHandle();
        if (MonitorFromWindow(handle, 2 /* NEAREST */) != MonitorFromPoint(new NativePoint { X = area.Left, Y = area.Top }, 1 /* PRIMARY */)) SetWindowPos(handle, IntPtr.Zero, area.Left, area.Top, 0, 0, 0x0001 | 0x0004 | 0x0010 /* NOSIZE NOZORDER NOACTIVATE */);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return new Point(area.Right / scale, area.Bottom / scale);
    }

    // Full-screen apps, presentations and Do not disturb: cards nobody clicked for are skipped
    static bool IsQuiet() { try { return SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4 or 6; } catch { return false; } }
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
