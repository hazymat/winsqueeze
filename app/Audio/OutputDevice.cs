using System.Diagnostics;
using System.Text.RegularExpressions;
using WinSqueeze.Settings;
namespace WinSqueeze.Audio;

public record OutputDevice(int Index, string Name, string HostApi)
{
    // Squeezelite matches -o by prefix on "name [host API]", so this survives index changes
    public string SqueezeliteId => $"{Name} [{HostApi}]";
    // The "Windows default" choice: stored as an empty device name, played through whatever App.DefaultDevice() finds
    public static OutputDevice WindowsDefault(string currentName) => new(-1, currentName == "" ? L.T("Windows default") : L.T("Windows default ({device})", ("device", currentName)), DefaultHostApi);
    public const string DefaultHostApi = "default";
    public bool IsWindowsDefault => HostApi == DefaultHostApi;
    public bool SupportsExclusive => HostApi == "Windows WASAPI";
    public string Driver => HostApi switch { "Windows WASAPI" => "WASAPI", "ASIO" => "ASIO", "Windows DirectSound" => "DirectSound", "MME" => "MME", "Windows WDM-KS" => "WDM-KS", DefaultHostApi => L.T("Default"), _ => HostApi };
    public int DriverOrder => Driver switch { "WASAPI" => 0, "ASIO" => 1, "DirectSound" => 2, "MME" => 3, _ => 4 };
    public string DisplayName => Name; // the list is grouped by driver; whether exclusive is in use shows in the player status
    public bool Matches(string deviceName, string hostApi) => Name == deviceName && HostApi == hostApi;
}

public static partial class OutputDeviceList
{
    [GeneratedRegex(@"^\s*(\d+) - (.+) \[([^\]]+)\]\s*$")] private static partial Regex DeviceLineRegex();

    // Asks the bundled Squeezelite (PortAudio) for its device list, so names are exactly what -o expects
    public static async Task<List<OutputDevice>> ListAsync()
    {
        var startInfo = new ProcessStartInfo(AppPaths.SqueezeliteExe, "-l") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = AppPaths.SqueezeliteFolder };
        using var listProcess = Process.Start(startInfo)!;
        var listOutput = await listProcess.StandardOutput.ReadToEndAsync();
        await listProcess.WaitForExitAsync();
        var devices = new List<OutputDevice>();
        foreach (var outputLine in listOutput.Split('\n'))
        {
            var lineMatch = DeviceLineRegex().Match(outputLine.TrimEnd('\r'));
            if (lineMatch.Success) devices.Add(new OutputDevice(int.Parse(lineMatch.Groups[1].Value), lineMatch.Groups[2].Value, lineMatch.Groups[3].Value));
        }
        return devices.OrderBy(device => device.DriverOrder).ThenBy(device => device.Name).ToList();
    }
}
