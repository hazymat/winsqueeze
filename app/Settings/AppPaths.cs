using System.IO;
namespace WinSqueeze.Settings;

public static class AppPaths
{
    // portable.txt next to the exe keeps settings and cache beside it (SPEC.md › Install)
    public static readonly bool IsPortable = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));
    public static readonly string DataFolder = IsPortable ? Path.Combine(AppContext.BaseDirectory, "data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinSqueeze");
    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");
    public static string LogsFolder => Directory.CreateDirectory(Path.Combine(DataFolder, "logs")).FullName;
    public static string SqueezeliteFolder => Path.Combine(AppContext.BaseDirectory, "squeezelite");
    public static string SqueezeliteExe => Path.Combine(SqueezeliteFolder, "squeezelite-ffmpeg-x64.exe");
    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WinSqueeze.exe");
}
