using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace WinSqueeze.Settings;

public class ServerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Home";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 9000;
    public string Username { get; set; } = "";
    public string ProtectedPassword { get; set; } = ""; // DPAPI, current user only
    // Remembered output for this profile, by name and driver, never by index
    public string DeviceName { get; set; } = "";
    public string DeviceHostApi { get; set; } = "";
    [JsonIgnore] public string Password
    {
        get { if (ProtectedPassword == "") return ""; try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedPassword), null, DataProtectionScope.CurrentUser)); } catch { return ""; } }
        set { ProtectedPassword = value == "" ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)); }
    }
    public override string ToString() => Host == "" ? Name : $"{Name} ({Host}:{Port})";
}

public class AudioSettings
{
    public bool Exclusive { get; set; } = true;
    public int LatencyMs { get; set; } = 200; // 40 ms made the DragonFly restart its stream every 10 s
    public string PlayerName { get; set; } = Environment.MachineName; // shown in the server's player list; user can rename
    public string ExtraArguments { get; set; } = "";
    public string FallbackDeviceName { get; set; } = ""; // empty = wait for the chosen device, never play elsewhere silently
    public string FallbackDeviceHostApi { get; set; } = "";
}

public class GeneralSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimised { get; set; }
    public bool CloseHidesToTray { get; set; } = true;
    public bool CloseToTrayTipShown { get; set; }
    public OtherSqueezeliteAction OtherSqueezelite { get; set; } = OtherSqueezeliteAction.Ask;
}

public enum OtherSqueezeliteAction { Ask, Stop, Leave }

public class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    public List<ServerProfile> Profiles { get; set; } = new();
    public string DefaultProfileId { get; set; } = "";
    public string PlayerMac { get; set; } = ""; // own stable MAC so we never collide with another Squeezelite on this PC

    [JsonIgnore] public ServerProfile? ActiveProfile => Profiles.FirstOrDefault(profile => profile.Id == DefaultProfileId) ?? Profiles.FirstOrDefault();

    static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static AppSettings Load()
    {
        AppSettings settings;
        try { settings = File.Exists(AppPaths.SettingsFile) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), jsonOptions) ?? new() : new(); }
        catch { settings = new(); }
        if (settings.PlayerMac == "") { settings.PlayerMac = NewLocalMac(); settings.Save(); }
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataFolder);
        var tempFile = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(tempFile, JsonSerializer.Serialize(this, jsonOptions));
        File.Move(tempFile, AppPaths.SettingsFile, true);
    }

    // Locally administered unicast MAC (02:...)
    static string NewLocalMac()
    {
        var macBytes = RandomNumberGenerator.GetBytes(6);
        macBytes[0] = 0x02;
        return string.Join(":", macBytes.Select(macByte => macByte.ToString("x2")));
    }
}
