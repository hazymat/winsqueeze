using NAudio.CoreAudioApi;
using WinSqueeze.Shell;
namespace WinSqueeze.Audio;

// Bit-perfect volume (SPEC.md › Playback and fidelity): when the output has hardware volume (e.g. the DragonFly), Squeezelite plays at full level (-F)
// and the server's volume for this player is applied to the Windows endpoint instead, which works even in exclusive mode.
// Only the endpoint id is kept for changes: the COM objects are opened fresh on the calling thread, because status arrives off the UI thread.
// It is the Windows volume for that device, so it works both ways: turning Windows' volume raises ExternalChange, which the app sends to the server
public sealed class DeviceVolume
{
    string? endpointId;
    int lastVolume = -1;
    readonly object applyLock = new();
    MMDeviceEnumerator? watchEnumerator; MMDevice? watchedDevice;
    System.Threading.Timer? externalTimer; int pendingExternal;
    public event Action<int>? ExternalChange; // server-scale volume (negative = muted) set from Windows, not by us

    // True when this device can take volume in hardware
    public bool Attach(string deviceName)
    {
        endpointId = null; lastVolume = -1;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var friendlyName = endpoint.FriendlyName;
                // PortAudio's MME names are cut to 31 characters, so match on either being the start of the other
                if (!(friendlyName.StartsWith(deviceName, StringComparison.OrdinalIgnoreCase) || deviceName.StartsWith(friendlyName, StringComparison.OrdinalIgnoreCase))) continue;
                if (endpoint.AudioEndpointVolume.HardwareSupport.HasFlag(EEndpointHardwareSupport.Volume)) endpointId = endpoint.ID;
                break;
            }
            if (endpointId != null)
            {
                StopWatching();
                watchEnumerator = new MMDeviceEnumerator(); watchedDevice = watchEnumerator.GetDevice(endpointId);
                watchedDevice.AudioEndpointVolume.OnVolumeNotification += OnWindowsVolume;
            }
        }
        catch (Exception error) { endpointId = null; CrashReporter.Write("Hardware volume attach failed", error, false); }
        return endpointId != null;
    }

    public void Detach() { endpointId = null; lastVolume = -1; StopWatching(); }

    void StopWatching()
    {
        try { if (watchedDevice != null) watchedDevice.AudioEndpointVolume.OnVolumeNotification -= OnWindowsVolume; watchedDevice?.Dispose(); watchEnumerator?.Dispose(); } catch { }
        watchedDevice = null; watchEnumerator = null;
    }

    // Windows' volume moved (mixer, keyboard keys, another app): our own Apply echoes back with the same value and is ignored; a drag is sent once it settles
    void OnWindowsVolume(AudioVolumeNotificationData data)
    {
        var volume = (int)Math.Round(data.MasterVolume * 100); if (data.Muted) volume = -Math.Max(volume, 1);
        lock (applyLock)
        {
            if (endpointId == null || volume == lastVolume) return;
            lastVolume = volume; pendingExternal = volume;
            externalTimer ??= new System.Threading.Timer(_ => { int settled; lock (applyLock) settled = pendingExternal; ExternalChange?.Invoke(settled); });
            externalTimer.Change(150, System.Threading.Timeout.Infinite);
        }
    }
    public bool Active => endpointId != null;

    // Server volume 0..100 (negative = muted in Lyrion) onto the endpoint's perceptual 0..1 scale
    // False when the endpoint refused, so the caller can fall back to Squeezelite's own volume
    public bool Apply(int serverVolume)
    {
        lock (applyLock)
        {
            if (endpointId == null || serverVolume == lastVolume) return true;
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDevice(endpointId);
                var endpointVolume = device.AudioEndpointVolume;
                endpointVolume.Mute = serverVolume < 0;
                if (serverVolume >= 0) endpointVolume.MasterVolumeLevelScalar = Math.Clamp(serverVolume / 100f, 0, 1);
                lastVolume = serverVolume; return true;
            }
            catch (Exception error) { CrashReporter.Write("Hardware volume change failed", error, false); return false; }
        }
    }
}
