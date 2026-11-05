using Microsoft.Win32;
namespace WinSqueeze.Settings;

// Per-user "Start with Windows" via HKCU Run, no admin rights
public static class StartupRegistration
{
    const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string valueName = "WinSqueeze";
    public static void Apply(bool startWithWindows)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(runKeyPath);
        if (startWithWindows) runKey.SetValue(valueName, $"\"{AppPaths.ExePath}\" --minimised"); else runKey.DeleteValue(valueName, false);
    }
}
