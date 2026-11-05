using NAudio.CoreAudioApi;
namespace WinSqueeze.Audio;

// Bit-perfect volume (SPEC.md › Playback and fidelity): when the output has hardware volume (e.g. the DragonFly), Squeezelite plays at full level (-F)
// and the server's volume for this player is applied to the Windows endpoint instead, which works even in exclusive mode
public sealed class DeviceVolume
{
    AudioEndpointVolume? endpointVolume;
    int lastVolume = -1;

    // True when this device can take volume in hardware
    public bool Attach(string deviceName)
    {
        endpointVolume = null; lastVolume = -1;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var friendlyName = endpoint.FriendlyName;
                // PortAudio's MME names are cut to 31 characters, so match on either being the start of the other
                if (!(friendlyName.StartsWith(deviceName, StringComparison.OrdinalIgnoreCase) || deviceName.StartsWith(friendlyName, StringComparison.OrdinalIgnoreCase))) continue;
                if (endpoint.AudioEndpointVolume.HardwareSupport.HasFlag(EEndpointHardwareSupport.Volume)) endpointVolume = endpoint.AudioEndpointVolume;
                break;
            }
        }
        catch { endpointVolume = null; }
        return endpointVolume != null;
    }

    public void Detach() { endpointVolume = null; lastVolume = -1; }
    public bool Active => endpointVolume != null;

    // Server volume 0..100 (negative = muted in Lyrion) onto the endpoint's perceptual 0..1 scale
    public void Apply(int serverVolume)
    {
        if (endpointVolume == null || serverVolume == lastVolume) return;
        lastVolume = serverVolume;
        try { endpointVolume.Mute = serverVolume < 0; if (serverVolume >= 0) endpointVolume.MasterVolumeLevelScalar = Math.Clamp(serverVolume / 100f, 0, 1); } catch { }
    }
}
