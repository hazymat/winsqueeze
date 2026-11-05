using System.IO;
namespace WinSqueeze.Settings;

public static class AppPaths
{
    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WinSqueeze.exe");
    public static string ExeFolder => Path.GetDirectoryName(ExePath) ?? AppContext.BaseDirectory;
#if PORTABLE
    public const bool IsPortable = true;
#else
    // portable.txt next to the exe also forces portable data, for a hand-copied folder
    public static readonly bool IsPortable = File.Exists(Path.Combine(ExeFolder, "portable.txt"));
#endif
    // Portable: data next to the exe. Installed: outside Velopack's own folder, so an update or reinstall never touches it
    public static readonly string DataFolder = IsPortable ? Path.Combine(ExeFolder, "WinSqueeze-data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinSqueeze-data");
    public static string LegacyDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinSqueeze");
    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");
    public static string LogsFolder => Directory.CreateDirectory(Path.Combine(DataFolder, "logs")).FullName;
    // Single-file builds unpack bundled content here; installed builds keep it beside the exe
    public static string SqueezeliteFolder => Path.Combine(AppContext.BaseDirectory, "squeezelite");
    public static string SqueezeliteExe => Path.Combine(SqueezeliteFolder, "squeezelite-ffmpeg-x64.exe");
}
