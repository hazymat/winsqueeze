using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
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
    // Devices that wouldn't open in exclusive mode this run, with the reason in plain words: they play shared until the app restarts
    readonly Dictionary<string, string> exclusiveRefused = new();
    public bool ExclusiveRefused => Device != null && exclusiveRefused.ContainsKey(Device.SqueezeliteId);
    public string ExclusiveRefusedReason => Device != null && exclusiveRefused.TryGetValue(Device.SqueezeliteId, out var refusedReason) ? refusedReason : "";
    public event Action<string, string>? ExclusiveFailed; // device name + reason, on Squeezelite's reader thread; the app restarts it shared
    string lastWasapiError = ""; // HRESULT our Squeezelite logs when PortAudio only says "Invalid device"
    public int OutputSampleRate { get; private set; }
    public int OutputLatencyMs { get; private set; } // what PortAudio reports once the device is open (shared mode adds to the requested latency)
    public event Action? StatusChanged;
    public event Action<string>? DecodeLine; // lines DamagedFiles cares about, on Squeezelite's reader thread

    [GeneratedRegex(@"opened device \d+ - (.+) at (\d+) latency(?: (\d+) ms)?")] private static partial Regex OpenedDeviceRegex();
    [GeneratedRegex(@"WASAPI exclusive open .*hr 0x([0-9A-Fa-f]{8})")] private static partial Regex WasapiErrorRegex();

    // Plain words for the Windows (AUDCLNT_E_*) error behind a refused exclusive open
    public static string ExclusiveRefusalText(string hresult) => hresult.ToUpperInvariant() switch
    {
        "8889000A" => "another program has it in exclusive mode, often the DAC's own ASIO driver or another player",
        "8889000E" => "Windows doesn't allow it: tick \"Allow applications to take exclusive control\" in Control Panel › Sound › device Properties › Advanced",
        "88890008" => "its driver accepts none of the sample formats WinSqueeze offered",
        "88890016" or "88890019" or "88890020" => "its driver refused the buffer size; try another Buffer (ms) in Settings › Audio",
        "88890004" => "it was unplugged or switched off",
        "" or "00000000" => "the driver gave no reason",
        _ => $"Windows error 0x{hresult.ToUpperInvariant()}"
    };

    // serverAddress: the Lyrion host, or 127.0.0.1:<port> for the offline server
    // startHeld: Squeezelite plays silence until ReleaseHold, so whatever the server restarts on connect isn't heard yet
    public void Start(string serverAddress, OutputDevice device, AudioSettings audio, string playerMac, bool startHeld = false)
    {
        Stop();
        Device = device; ExclusiveActive = false; OutputSampleRate = 0; OutputLatencyMs = 0;
        lastWasapiError = "";
        var useExclusive = audio.Exclusive && device.SupportsExclusive && !exclusiveRefused.ContainsKey(device.SqueezeliteId);
        var startInfo = new ProcessStartInfo(AppPaths.SqueezeliteExe) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = AppPaths.SqueezeliteFolder };
        // -s takes the slimproto host (port 3483), not the web port
        foreach (var argument in new[] { "-s", serverAddress, "-n", audio.PlayerName, "-m", playerMac, "-o", device.SqueezeliteId, "-a", $"{audio.LatencyMs}:{(useExclusive ? 1 : 0)}", "-d", "output=info", "-d", "stream=info", "-d", "decode=info" }) startInfo.ArgumentList.Add(argument);
        var extraArguments = SplitArguments(audio.ExtraArguments).ToList();
        // Resample only tracks the device can't take natively (e.g. 22.05 kHz radio in exclusive mode); without it Squeezelite retries the open forever
        if (!extraArguments.Any(argument => argument is "-R" or "-u")) foreach (var argument in new[] { "-R", "hE" }) startInfo.ArgumentList.Add(argument);
        // Exclusive mode: let go of the device 10 s after a pause or stop, so other apps can play without closing WinSqueeze
        if (useExclusive && !extraArguments.Contains("-C")) foreach (var argument in new[] { "-C", "10" }) startInfo.ArgumentList.Add(argument);
        if (SupportsVisualiser) startInfo.ArgumentList.Add("-v"); // share played samples for the meters
        if (startHeld && SupportsHold) startInfo.ArgumentList.Add("-H");
        foreach (var extraArgument in extraArguments) startInfo.ArgumentList.Add(extraArgument);
        logWriter = new StreamWriter(Path.Combine(AppPaths.LogsFolder, "squeezelite.log"), false) { AutoFlush = true };
        logWriter.WriteLine($"[{DateTime.Now:O}] start: {string.Join(" ", startInfo.ArgumentList.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument))}");
        stopping = false;
        squeezeliteProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        squeezeliteProcess.OutputDataReceived += (sender, args) => OnLogLine(args.Data);
        squeezeliteProcess.ErrorDataReceived += (sender, args) => OnLogLine(args.Data);
        squeezeliteProcess.Exited += (sender, args) => { if (sender == squeezeliteProcess) OnExited(); }; // not a process Stop already replaced
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
    public static bool SupportsHold => (helpText ??= ReadHelpText()).Contains("WINHOLD"); // rides on -v

    // Clears the startup hold: "hold" sits after the EQ block in "Local\squeezelite-eq-<mac>" (vendor/squeezelite/src/output_eq_win.c)
    public static void ReleaseHold(string playerMac)
    {
        const int HoldOffset = 24 + 2 * 12 * 5 * 4;
        try
        {
            using var sharedFile = MemoryMappedFile.OpenExisting("Local\\squeezelite-eq-" + playerMac.Replace(":", "").ToLowerInvariant(), MemoryMappedFileRights.ReadWrite);
            using var view = sharedFile.CreateViewAccessor(HoldOffset, 4, MemoryMappedFileAccess.ReadWrite);
            view.Write(0, 0);
        }
        catch (Exception) { } // Squeezelite gone or not started held: nothing to release
    }
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
        var wasapiErrorMatch = WasapiErrorRegex().Match(logLine);
        if (wasapiErrorMatch.Success) lastWasapiError = wasapiErrorMatch.Groups[1].Value;
        if (logLine.Contains("opening WASAPI device in exclusive mode")) ExclusiveActive = true;
        else if (openedMatch.Success) { OutputSampleRate = int.Parse(openedMatch.Groups[2].Value); if (openedMatch.Groups[3].Success) OutputLatencyMs = int.Parse(openedMatch.Groups[3].Value); if (State == PlayerState.Failed) SetStatus(PlayerState.Running, $"Playing to {openedMatch.Groups[1].Value}"); else StatusChanged?.Invoke(); }
        // "unable to open output device": every rate refused at startup, so Squeezelite exits
        else if (ExclusiveActive && Device != null && (logLine.Contains("error opening device") || logLine.Contains("unable to open output device")) && exclusiveRefused.TryAdd(Device.SqueezeliteId, ExclusiveRefusalText(lastWasapiError))) ExclusiveFailed?.Invoke(Device.Name, exclusiveRefused[Device.SqueezeliteId]);
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
