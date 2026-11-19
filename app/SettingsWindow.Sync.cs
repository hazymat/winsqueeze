using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinSqueeze.Library;
using WinSqueeze.Offline;
using WinSqueeze.Settings;
using WinSqueeze.Shell;
namespace WinSqueeze;

// Settings › Library & cache › Library sync (SPEC.md › Library sync problems): the one place the details of a sync failure live; the main window's amber bar and Settings dot lead here
public partial class SettingsWindow
{
    ConnectionReport? syncReport; // the latest connection checks, while sync is failing
    bool syncChecking, syncRetrying;
    static readonly Color amber = (Color)ColorConverter.ConvertFromString("#E0912F"), green = (Color)ColorConverter.ConvertFromString("#3FB950");

    void WireSyncCard()
    {
        // The amber LED breathes like the hi-fi standby lamp (docs/DESIGN.md); green stays still
        var glowPulse = new DoubleAnimation(3, 10, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        var glowOpacity = new DoubleAnimation(0.35, 0.85, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        void Led(bool failing)
        {
            var colour = failing ? amber : green;
            syncLed.Fill = new RadialGradientBrush(new GradientStopCollection { new(Color.FromRgb(255, 240, 215), 0), new(colour, 0.55), new(Color.FromRgb((byte)(colour.R / 2), (byte)(colour.G / 2), (byte)(colour.B / 2)), 1) }) { GradientOrigin = new Point(0.35, 0.35), Center = new Point(0.4, 0.4) };
            syncLedGlow.Color = colour;
            syncLedGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, failing ? glowPulse : null);
            syncLedGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, failing ? glowOpacity : null);
            if (!failing) { syncLedGlow.BlurRadius = 0; syncLedGlow.Opacity = 0; }
        }
        var wasFailing = (bool?)null;
        void Render()
        {
            var library = App.Library; var failing = library.FailingSince != null;
            if (wasFailing != failing) { Led(failing); wasFailing = failing; }
            libraryTab.Tag = failing ? "alert" : null;
            syncCard.BorderBrush = new SolidColorBrush(failing ? (Color)ColorConverter.ConvertFromString("#EBC18A") : (Color)ColorConverter.ConvertFromString("#E3E5E8"));
            syncStateText.Text = syncRetrying ? L.T("Trying again…") : failing ? L.T("Not working since {time}", ("time", library.FailingSince!.Value.ToString("t"))) + " (" + Ago(library.FailingSince.Value) + ")" : library.Syncing ? L.T("Syncing…") : L.T("Working");
            var lastSync = library.LastGoodSync is { } synced ? (synced.Date == DateTime.Today ? L.T("today {time}", ("time", synced.ToString("t"))) : synced.ToString("g")) : L.T("not yet");
            syncQuietText.Text = !failing ? L.T("Last sync: {when}", ("when", lastSync)) + " · " + L.T("Checks for changes every {count} min", ("count", App.Settings.Library.RefreshMinutes))
                : string.Join(" · ", new[] { L.T("Last good sync: {when}", ("when", lastSync)), L.N(library.FailedTries, "Tried once", "Tried {count} times"), App.IsOffline ? L.T("Tries again when WinSqueeze is back online") : library.NextTry is { } next ? L.T("Next try at {time}", ("time", next.ToString("t"))) : "", L.T("Your library still works from what's already on this PC") }.Where(part => part != ""));
            // Checks: the three connection steps, then (while trying again) the sync itself with its progress
            syncChecks.Children.Clear();
            var showChecks = failing || syncRetrying;
            syncChecksBox.Visibility = showChecks && (syncChecking || syncReport != null || syncRetrying) ? Visibility.Visible : Visibility.Collapsed;
            if (syncChecking) CheckRow("…", "#2F7FD8", L.T("Checking the connection…"), "");
            else if (syncReport is { } report && App.Settings.ActiveProfile is { } profile)
            {
                CheckRow(report.Network ? "✓" : "✕", report.Network ? "#2E8B3E" : "#C0392B", L.T("Network"), report.Network ? L.T("Connected") + (report.NetworkKind != "" ? " · " + report.NetworkKind : "") : L.T("Not connected"));
                CheckRow(report.Ping ? "✓" : "✕", report.Ping ? "#2E8B3E" : "#C0392B", L.T("Server PC"), report.Ping ? L.T("{host} answers ping", ("host", profile.Host)) + " · " + L.T("{count} ms", ("count", Math.Max(1, report.PingMs))) : L.T("{host} doesn't answer ping", ("host", profile.Host)));
                var lyrionProblem = report.LyrionProblem switch { "refused" => L.T("nothing is listening"), "notfound" => L.T("name not found"), "password" => L.T("wrong user name or password"), "http" => L.T("error {code}", ("code", report.HttpStatus)), "noreply" => L.T("timed out after {seconds} s", ("seconds", 10)), "timeout" => L.T("timed out after {seconds} s", ("seconds", 4)), _ => "" };
                CheckRow(report.Lyrion ? "✓" : "✕", report.Lyrion ? "#2E8B3E" : "#C0392B", "Lyrion", report.Lyrion ? L.T("Answering on port {port}", ("port", profile.Port)) : L.T("No reply on port {port}", ("port", profile.Port)) + (lyrionProblem != "" ? " · " + lyrionProblem : ""));
                if (syncRetrying && report.Lyrion) CheckRow(library.Syncing ? "●" : "○", "#2F7FD8", L.T("Syncing with Lyrion"), library.ProgressText + (library.ProgressTotal > 1 ? " · " + L.T("{done} of {total}", ("done", library.ProgressDone.ToString("N0")), ("total", library.ProgressTotal.ToString("N0"))) : ""));
            }
            syncProgressBar.Visibility = syncRetrying && library.ProgressTotal > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (library.ProgressTotal > 1) { syncProgressBar.Maximum = library.ProgressTotal; syncProgressBar.Value = library.ProgressDone; }
            // Verdict in plain words, then the error's own words
            syncVerdictBox.Visibility = failing && !syncRetrying ? Visibility.Visible : Visibility.Collapsed;
            syncVerdictText.Text = syncReport is not { } checkedReport ? L.T("Library sync isn't working: {problem}", ("problem", library.ProblemText))
                : checkedReport.Lyrion ? L.T("Lyrion is answering now. Try again now should fix it.") : !checkedReport.Network ? L.T("This PC isn't connected to a network.") : checkedReport.LyrionProblem == "password" ? L.T("Lyrion turned down the user name or password.") : checkedReport.Ping ? L.T("The server PC is on, but Lyrion isn't answering.") : L.T("The server PC can't be reached.");
            syncErrorText.Text = library.ErrorText;
            syncNowButton.Content = failing ? L.T("Try again now") : L.T("Sync now");
            syncNowButton.IsEnabled = !syncRetrying && !library.Syncing && !App.IsOffline;
            syncLogExpander.Visibility = failing ? Visibility.Visible : Visibility.Collapsed;
            if (failing) syncLogText.Text = LibraryService.LogTail(8);
        }
        void CheckRow(string glyph, string colour, string what, string detail)
        {
            var row = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
            row.Inlines.Add(new Run(glyph) { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour)), FontWeight = FontWeights.Bold });
            row.Inlines.Add(new Run("   " + what) { FontWeight = FontWeights.SemiBold });
            if (detail != "") row.Inlines.Add(new Run("   " + detail) { Foreground = Brushes.DimGray });
            syncChecks.Children.Add(row);
        }
        static string Ago(DateTime since) { var minutes = (int)Math.Max(1, (DateTime.Now - since).TotalMinutes); return minutes < 120 ? L.N(minutes, "{count} minute", "{count} minutes") : minutes < 2880 ? L.N(minutes / 60, "{count} hour", "{count} hours") : L.N(minutes / 1440, "{count} day", "{count} days"); }
        async Task RunChecksAsync()
        {
            if (syncChecking || App.Settings.ActiveProfile is not { Host.Length: > 0 } profile) return;
            syncChecking = true; Render();
            try { syncReport = await ConnectionCheck.RunAsync(profile); } finally { syncChecking = false; Render(); }
        }
        void OnLibraryChanged() => Dispatcher.BeginInvoke(() => { if (App.Library.FailingSince == null && !syncRetrying) syncReport = null; Render(); });
        App.Library.Changed += OnLibraryChanged;
        Closed += (sender, args) => App.Library.Changed -= OnLibraryChanged;
        settingsTabs.SelectionChanged += async (sender, args) => { if (args.Source == settingsTabs && settingsTabs.SelectedItem == libraryTab && App.Library.FailingSince != null) await RunChecksAsync(); };
        syncNowButton.Click += async (sender, args) =>
        {
            syncRetrying = true; syncReport = null;
            try { await RunChecksAsync(); if (syncReport?.Lyrion != false) await App.Library.RefreshAsync(false); else LibraryService.Log("Try again now: Lyrion still can't be reached"); }
            finally { syncRetrying = false; if (App.Library.FailingSince == null) syncReport = null; Render(); }
        };
        openSyncLogButton.Click += (sender, args) => { var logPath = Path.Combine(AppPaths.LogsFolder, "library.log"); Process.Start(new ProcessStartInfo(File.Exists(logPath) ? logPath : AppPaths.LogsFolder) { UseShellExecute = true }); };
        copySyncButton.Click += (sender, args) => { try { Clipboard.SetText(string.Join(Environment.NewLine, new[] { "WinSqueeze library sync", syncStateText.Text, syncQuietText.Text, string.Join(Environment.NewLine, syncChecks.Children.OfType<TextBlock>().Select(row => new TextRange(row.ContentStart, row.ContentEnd).Text)), App.Library.ErrorText, "--- library.log (end) ---", LibraryService.LogTail(30) }.Where(part => part != ""))); } catch { } };
        Render();
        if (App.Library.FailingSince != null && settingsTabs.SelectedItem == libraryTab) _ = RunChecksAsync();
    }
}
