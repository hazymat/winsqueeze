using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
namespace WinSqueeze.Audio;

// The output Windows plays to (Settings › System › Sound), used until the user picks a device; Changed fires when it's switched in Windows
public sealed class WindowsDefaultDevice : IMMNotificationClient, IDisposable
{
    readonly MMDeviceEnumerator deviceEnumerator = new();
    public event Action? Changed; // on a COM thread
    public WindowsDefaultDevice() { try { deviceEnumerator.RegisterEndpointNotificationCallback(this); } catch { } }
    // Friendly name as Windows and PortAudio's WASAPI list give it; "" when Windows has no output at all
    public string Name { get { try { using var endpoint = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); return endpoint.FriendlyName; } catch { return ""; } } }
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { if (flow == DataFlow.Render && role == Role.Multimedia) Changed?.Invoke(); }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    public void Dispose() { try { deviceEnumerator.UnregisterEndpointNotificationCallback(this); } catch { } deviceEnumerator.Dispose(); }
}
