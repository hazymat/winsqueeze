using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using WinSqueeze.Settings;
namespace WinSqueeze.Audio;

public enum PlayerState { Stopped, Running, WaitingForDevice, Failed }

// Runs the bundled Squeezelite hidden, captures its log, reports output mode and rate
public sealed partial class SqueezeliteHost : IDisposable
{
    readonly ProcessJob processJob = new();
    Process? squeezeliteProcess;
    StreamWriter? logWriter;
    bool stopping;

    public PlayerState State { get; private set; } = PlayerState.Stopped;
    public string StatusText { get; private set; } = "Stopped";
    public OutputDevice? Device { get; private set; }
    public bool ExclusiveActive { get; private set; }
    public int OutputSampleRate { get; private set; }
    public event Action? StatusChanged;
    public event Action<string>? DecodeLine; // lines DamagedFiles cares about, on Squeezelite's reader thread

    [GeneratedRegex(@"opened device \d+ - (.+) at (\d+) latency")] private static partial Regex OpenedDeviceRegex();

    // serverAddress: the Lyrion host, or 127.0.0.1:<port> for the offline server
    public void Start(string serverAddress, OutputDevice device, AudioSettings audio, string playerMac)
    {
        Stop();
        Device = device; ExclusiveActive = false; OutputSampleRate = 0;
        var startInfo = new ProcessStartInfo(AppPaths.SqueezeliteExe) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = AppPaths.SqueezeliteFolder };
        // -s takes the slimproto host (port 3483), not the web port
        foreach (var argument in new[] { "-s", serverAddress, "-n", audio.PlayerName, "-m", playerMac, "-o", device.SqueezeliteId, "-a", $"{audio.LatencyMs}:{(audio.Exclusive && device.SupportsExclusive ? 1 : 0)}", "-d", "output=info", "-d", "stream=info", "-d", "decode=info" }) startInfo.ArgumentList.Add(argument);
        var extraArguments = SplitArguments(audio.ExtraArguments).ToList();
        // Resample only tracks the device can't take natively (e.g. 22.05 kHz radio in exclusive mode); without it Squeezelite retries the open forever
        if (!extraArguments.Any(argument => argument is "-R" or "-u")) foreach (var argument in new[] { "-R", "hE" }) startInfo.ArgumentList.Add(argument);
        // Exclusive mode: let go of the device 10 s after a pause or stop, so other apps can play without closing WinSqueeze
        if (audio.Exclusive && device.SupportsExclusive && !extraArguments.Contains("-C")) foreach (var argument in new[] { "-C", "10" }) startInfo.ArgumentList.Add(argument);
        if (SupportsVisualiser) startInfo.ArgumentList.Add("-v"); // share played samples for the meters
        foreach (var extraArgument in extraArguments) startInfo.ArgumentList.Add(extraArgument);
        logWriter = new StreamWriter(Path.Combine(AppPaths.LogsFolder, "squeezelite.log"), false) { AutoFlush = true };
        logWriter.WriteLine($"[{DateTime.Now:O}] start: {string.Join(" ", startInfo.ArgumentList.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument))}");
        stopping = false;
        squeezeliteProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        squeezeliteProcess.OutputDataReceived += (sender, args) => OnLogLine(args.Data);
        squeezeliteProcess.ErrorDataReceived += (sender, args) => OnLogLine(args.Data);
        squeezeliteProcess.Exited += (sender, args) => OnExited();
        squeezeliteProcess.Start();
        processJob.Add(squeezeliteProcess);
        squeezeliteProcess.BeginOutputReadLine(); squeezeliteProcess.BeginErrorReadLine();
        SetStatus(PlayerState.Running, $"Playing to {device.Name}");
    }

    public void Stop()
    {
        if (squeezeliteProcess is { HasExited: false }) { stopping = true; try { squeezeliteProcess.Kill(); squeezeliteProcess.WaitForExit(3000); } catch (InvalidOperationException) { } }
        squeezeliteProcess?.Dispose(); squeezeliteProcess = null;
        logWriter?.Dispose(); logWriter = null;
        if (State == PlayerState.Running) SetStatus(PlayerState.Stopped, "Stopped");
    }

    public void SetWaiting(string message) { Stop(); SetStatus(PlayerState.WaitingForDevice, message); }

    // Only our own build has the Windows visualiser export; an unknown -v would make another build exit
    static string? helpText;
    public static bool SupportsVisualiser => (helpText ??= ReadHelpText()).Contains("VISEXPORT");
    public static bool SupportsEqualiser => (helpText ??= ReadHelpText()).Contains("WINEQ"); // rides on -v
    public static bool SupportsRecording => (helpText ??= ReadHelpText()).Contains("WINREC"); // rides on -v
    static string ReadHelpText()
    {
        try
        {
            using var helpProcess = Process.Start(new ProcessStartInfo(AppPaths.SqueezeliteExe, "-?") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
            var output = helpProcess.StandardOutput.ReadToEnd() + helpProcess.StandardError.ReadToEnd();
            helpProcess.WaitForExit(3000);
            return output;
        }
        catch { return ""; }
    }

    long logSecond, linesThisSecond, droppedLines;
    void OnLogLine(string? logLine)
    {
        if (logLine == null) return;
        // A retry loop can log ~100 lines a second: keep the first 50 of each second so the log stays readable and small
        var thisSecond = Environment.TickCount64 / 1000;
        if (thisSecond != logSecond) { if (droppedLines > 0) try { logWriter?.WriteLine($"(… {droppedLines} repeated lines skipped)"); } catch (ObjectDisposedException) { } logSecond = thisSecond; linesThisSecond = 0; droppedLines = 0; }
        if (++linesThisSecond <= 50) try { logWriter?.WriteLine(logLine); } catch (ObjectDisposedException) { } else droppedLines++;
        if (DamagedFiles.Matters(logLine)) DecodeLine?.Invoke(logLine);
        var openedMatch = OpenedDeviceRegex().Match(logLine);
        if (logLine.Contains("opening WASAPI device in exclusive mode")) ExclusiveActive = true;
        else if (openedMatch.Success) { OutputSampleRate = int.Parse(openedMatch.Groups[2].Value); if (State == PlayerState.Failed) SetStatus(PlayerState.Running, $"Playing to {openedMatch.Groups[1].Value}"); else StatusChanged?.Invoke(); }
        else if (logLine.Contains("AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED")) SetStatus(PlayerState.Failed, "Windows blocks exclusive mode for this device. Tick \"Allow applications to take exclusive control\" in Sound settings › device › Advanced.");
        else if (logLine.Contains("Invalid sample rate")) SetStatus(PlayerState.Failed, "The device rejected this track's sample rate. Remove any -R override from Extra arguments so WinSqueeze can resample it.");
    }

    void OnExited()
    {
        if (stopping) return;
        SetStatus(PlayerState.Failed, $"Squeezelite exited (code {squeezeliteProcess?.ExitCode}). See logs\\squeezelite.log.");
    }

    // Repeats are dropped: a failing device can log the same error dozens of times a second
    void SetStatus(PlayerState newState, string newText) { if (newState == State && newText == StatusText) return; State = newState; StatusText = newText; StatusChanged?.Invoke(); }

    // Splits "extra arguments" respecting double quotes
    static IEnumerable<string> SplitArguments(string argumentText)
    {
        foreach (Match argumentMatch in Regex.Matches(argumentText ?? "", "\"([^\"]*)\"|(\\S+)")) yield return argumentMatch.Groups[1].Success ? argumentMatch.Groups[1].Value : argumentMatch.Groups[2].Value;
    }

    public void Dispose() { Stop(); processJob.Dispose(); }
}
