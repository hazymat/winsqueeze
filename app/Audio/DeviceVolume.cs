using NAudio.CoreAudioApi;
using WinSqueeze.Shell;
namespace WinSqueeze.Audio;

// Bit-perfect volume (SPEC.md › Playback and fidelity): when the output has hardware volume (e.g. the DragonFly), Squeezelite plays at full level (-F)
// and the server's volume for this player is applied to the Windows endpoint instead, which works even in exclusive mode.
// Only the endpoint id is kept: the COM objects are opened fresh on the calling thread, because status arrives off the UI thread
public sealed class DeviceVolume
{
    string? endpointId;
    int lastVolume = -1;
    readonly object applyLock = new();

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
        }
        catch (Exception error) { endpointId = null; CrashReporter.Write("Hardware volume attach failed", error, false); }
        return endpointId != null;
    }

    public void Detach() { endpointId = null; lastVolume = -1; }
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
