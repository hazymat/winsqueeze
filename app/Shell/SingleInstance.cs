using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
namespace WinSqueeze.Shell;

// One WinSqueeze per user. A second launch brings the running copy to the front; if that copy doesn't answer (hung, or stuck while quitting) it is ended and the new launch takes its place
public sealed class SingleInstance : IDisposable
{
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    readonly Mutex instanceMutex;
    readonly EventWaitHandle showRequest = new(false, EventResetMode.AutoReset, "WinSqueeze.ShowWindow"), showAnswered = new(false, EventResetMode.AutoReset, "WinSqueeze.WindowShown");
    RegisteredWaitHandle? showWait;
    bool ownsMutex;

    public SingleInstance() { instanceMutex = new Mutex(true, "WinSqueeze.SingleInstance", out ownsMutex); }

    // true: this is now the only copy. false: the running copy came to the front, so this launch should exit
    public bool Claim()
    {
        if (ownsMutex || TryTakeMutex(TimeSpan.Zero)) return true;
        AllowSetForegroundWindow(-1); // ASFW_ANY: we're the app the user just launched, so let the running copy take the foreground
        showRequest.Set();
        if (showAnswered.WaitOne(TimeSpan.FromSeconds(8))) return false;
        var currentProcess = Process.GetCurrentProcess();
        foreach (var processName in new[] { currentProcess.ProcessName, "WinSqueeze", "WinSqueeze-Portable" }.Distinct())
            foreach (var otherCopy in Process.GetProcessesByName(processName).Where(process => process.Id != currentProcess.Id && process.SessionId == currentProcess.SessionId))
                try { otherCopy.Kill(true); otherCopy.WaitForExit(5000); } catch (Exception) { } // whole tree: its Squeezelite and WebView2 processes too
        return TryTakeMutex(TimeSpan.FromSeconds(5));
    }

    bool TryTakeMutex(TimeSpan timeout) { try { ownsMutex = instanceMutex.WaitOne(timeout); } catch (AbandonedMutexException) { ownsMutex = true; } return ownsMutex; } // abandoned = the old copy died holding it

    // Later launches call showWindow on a thread-pool thread; answering tells them this copy is alive
    public void OnShowRequest(Action showWindow) => showWait = ThreadPool.RegisterWaitForSingleObject(showRequest, (state, timedOut) => showWindow(), null, -1, false);
    public void Answer() => showAnswered.Set();

    // Before a restart for an update: the new version can claim straight away (must run on the thread that claimed)
    public void Release() { showWait?.Unregister(null); if (ownsMutex) { try { instanceMutex.ReleaseMutex(); } catch (ApplicationException) { } ownsMutex = false; } }
    public void Dispose() { Release(); instanceMutex.Dispose(); showRequest.Dispose(); showAnswered.Dispose(); }
}
