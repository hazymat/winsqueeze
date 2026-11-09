using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WinSqueeze.Settings;
namespace WinSqueeze.Audio;

public record StartupEntry(string Description, string ShortcutPath, string RunValueName);
public record OtherPlayer(int ProcessId, string ExePath, List<StartupEntry> StartupEntries);

// Finds other Squeezelite instances even when the exe has been renamed:
// candidates are processes talking slimproto (TCP 3483) or named *squeezelite*, confirmed by Squeezelite's own banner text inside the exe
public static class OtherPlayerDetector
{
    const int slimprotoPort = 3483;
    static readonly byte[][] signatures = { Encoding.ASCII.GetBytes("lightweight headless squeezebox emulator"), Encoding.ASCII.GetBytes("Squeezelite") };
    const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static List<OtherPlayer> Find(int ownChildProcessId)
    {
        var slimprotoProcessIds = SlimprotoProcessIds();
        var otherPlayers = new List<OtherPlayer>();
        var ownProcessId = Environment.ProcessId;
        foreach (var candidate in Process.GetProcesses())
        {
            using (candidate)
            {
                if (candidate.Id == ownProcessId || candidate.Id == ownChildProcessId || candidate.Id == 0) continue;
                var nameMatches = candidate.ProcessName.Contains("squeezelite", StringComparison.OrdinalIgnoreCase);
                if (!nameMatches && !slimprotoProcessIds.Contains(candidate.Id)) continue;
                var exePath = ProcessExePath(candidate.Id);
                if (exePath == null || !HasSqueezeliteSignature(exePath)) continue;
                otherPlayers.Add(new OtherPlayer(candidate.Id, exePath, FindStartupEntries(exePath)));
            }
        }
        return otherPlayers;
    }

    // Stops the process and, if asked, switches off what launches it at login. Returns problems for the user.
    public static List<string> Stop(OtherPlayer otherPlayer, bool disableStartup)
    {
        var problems = new List<string>();
        try { using var otherProcess = Process.GetProcessById(otherPlayer.ProcessId); otherProcess.Kill(); otherProcess.WaitForExit(3000); }
        catch (ArgumentException) { } // already gone
        catch (Exception stopError) { problems.Add(L.T("Could not stop {program}: {error}. It may be running as administrator.", ("program", Path.GetFileName(otherPlayer.ExePath)), ("error", stopError.Message))); }
        if (!disableStartup) return problems;
        var backupFolder = Directory.CreateDirectory(Path.Combine(AppPaths.DataFolder, "disabled-startup")).FullName;
        foreach (var startupEntry in otherPlayer.StartupEntries)
        {
            try
            {
                // Moved aside, never deleted, so it can be put back by hand
                if (startupEntry.ShortcutPath != "") File.Move(startupEntry.ShortcutPath, Path.Combine(backupFolder, Path.GetFileName(startupEntry.ShortcutPath)), true);
                else if (startupEntry.RunValueName != "")
                {
                    using var runKey = Registry.CurrentUser.OpenSubKey(runKeyPath, true);
                    var runCommand = runKey?.GetValue(startupEntry.RunValueName) as string;
                    if (runCommand == null) continue;
                    File.AppendAllText(Path.Combine(backupFolder, "run-entries.txt"), $"{startupEntry.RunValueName}={runCommand}{Environment.NewLine}");
                    runKey!.DeleteValue(startupEntry.RunValueName);
                }
            }
            catch (Exception disableError) { problems.Add(L.T("Could not switch off {entry}: {error}", ("entry", startupEntry.Description), ("error", disableError.Message))); }
        }
        return problems;
    }

    // User Startup-folder shortcuts and HKCU Run values that launch this exe (all-users entries need admin, so they are only reported)
    static List<StartupEntry> FindStartupEntries(string exePath)
    {
        var startupEntries = new List<StartupEntry>();
        foreach (var (startupFolder, isUserFolder) in new[] { (Environment.GetFolderPath(Environment.SpecialFolder.Startup), true), (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), false) })
        {
            if (!Directory.Exists(startupFolder)) continue;
            foreach (var shortcutPath in Directory.GetFiles(startupFolder, "*.lnk"))
                if (string.Equals(ShortcutTarget(shortcutPath), exePath, StringComparison.OrdinalIgnoreCase))
                    startupEntries.Add(new StartupEntry((isUserFolder ? L.T("Startup shortcut") : L.T("All-users Startup shortcut (needs admin)")) + " " + Path.GetFileName(shortcutPath), isUserFolder ? shortcutPath : "", ""));
        }
        using var runKey = Registry.CurrentUser.OpenSubKey(runKeyPath);
        foreach (var valueName in runKey?.GetValueNames() ?? Array.Empty<string>())
            if ((runKey!.GetValue(valueName) as string ?? "").Contains(exePath, StringComparison.OrdinalIgnoreCase)) startupEntries.Add(new StartupEntry(L.T("Run entry \"{name}\"", ("name", valueName)), "", valueName));
        return startupEntries;
    }

    static string? ShortcutTarget(string shortcutPath)
    {
        try { dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!; return (string)shell.CreateShortcut(shortcutPath).TargetPath; }
        catch { return null; }
    }

    static bool HasSqueezeliteSignature(string exePath)
    {
        try
        {
            var exeBytes = File.ReadAllBytes(exePath);
            if (exeBytes.Length > 64 * 1024 * 1024) return false;
            return signatures.All(signature => exeBytes.AsSpan().IndexOf(signature) >= 0);
        }
        catch { return false; }
    }

    static string? ProcessExePath(int processId)
    {
        var processHandle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (processHandle == IntPtr.Zero) return null;
        try { var pathBuilder = new StringBuilder(1024); var pathLength = pathBuilder.Capacity; return QueryFullProcessImageName(processHandle, 0, pathBuilder, ref pathLength) ? pathBuilder.ToString() : null; }
        finally { CloseHandle(processHandle); }
    }

    // Owning process ids of IPv4 TCP connections to remote port 3483
    static HashSet<int> SlimprotoProcessIds()
    {
        var processIds = new HashSet<int>();
        var tableSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref tableSize, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        var tablePointer = Marshal.AllocHGlobal(tableSize);
        try
        {
            if (GetExtendedTcpTable(tablePointer, ref tableSize, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0) return processIds;
            var rowCount = Marshal.ReadInt32(tablePointer);
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                var tableRow = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(tablePointer + 4 + rowIndex * rowSize);
                var remotePort = (int)(((tableRow.RemotePort & 0xFF) << 8) | ((tableRow.RemotePort >> 8) & 0xFF)); // network byte order
                if (remotePort == slimprotoPort) processIds.Add((int)tableRow.OwningProcessId);
            }
        }
        finally { Marshal.FreeHGlobal(tablePointer); }
        return processIds;
    }

    const int AF_INET = 2, TCP_TABLE_OWNER_PID_ALL = 5;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    [StructLayout(LayoutKind.Sequential)] struct MIB_TCPROW_OWNER_PID { public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, OwningProcessId; }
    [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table, ref int tableSize, bool sorted, int addressFamily, int tableClass, uint reserved);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}
