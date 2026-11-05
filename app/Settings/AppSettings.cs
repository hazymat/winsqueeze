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

public class LibrarySettings
{
    public string AlbumSource { get; set; } = "folders"; // folders | tags
    public string ContributorGrouping { get; set; } = "any"; // any | albumartist
    public string NameSeparators { get; set; } = ", ; & / feat. with and";
    public int RefreshMinutes { get; set; } = 5;
    public bool HideOwnComposers { get; set; } = true; // composers list leaves out songwriters credited only on their own albums
    // Second-level browsing: groupings offered in each view's Group menu, and each area's starting grouping (SPEC.md › Browsing)
    public List<string> BrowseGroupings { get; set; } = new() { "artist", "year", "decade", "added", "format", "initial" };
    public Dictionary<string, string> DefaultGrouping { get; set; } = new() { ["genre"] = "artist", ["artist"] = "none", ["composer"] = "artist", ["performer"] = "artist", ["albums"] = "none" };
}

public class GeneralSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimised { get; set; }
    public bool CloseHidesToTray { get; set; } = true;
    public bool CloseToTrayTipShown { get; set; }
    public OtherSqueezeliteAction OtherSqueezelite { get; set; } = OtherSqueezeliteAction.Ask;
    public bool AutoUpdate { get; set; } = true;
    public Dictionary<string, double[]> ModeSizes { get; set; } = new();
    public string UpdateChannel { get; set; } = "dev"; // stable | dev; dev until the first stable release
    public bool Glass { get; set; } = true; // Windows 11 acrylic backdrop behind a see-through UI
    // No title bar or standard border by default; the UI draws its own drag strip and buttons (stored as ShowTitleBar so older "Frameless": false files get the new default)
    public bool ShowTitleBar { get; set; }
    [JsonIgnore] public bool Frameless { get => !ShowTitleBar; set => ShowTitleBar = !value; }
    public string LastRunVersion { get; set; } = "";
}

public enum OtherSqueezeliteAction { Ask, Stop, Leave }

public class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    public LibrarySettings Library { get; set; } = new();
    public List<ServerProfile> Profiles { get; set; } = new();
    public string DefaultProfileId { get; set; } = "";
    public string PlayerMac { get; set; } = ""; // own stable MAC so we never collide with another Squeezelite on this PC

    [JsonIgnore] public ServerProfile? ActiveProfile => Profiles.FirstOrDefault(profile => profile.Id == DefaultProfileId) ?? Profiles.FirstOrDefault();

    static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static AppSettings Load()
    {
        // Settings from the first test builds lived in %LOCALAPPDATA%\WinSqueeze
        var legacySettings = Path.Combine(AppPaths.LegacyDataFolder, "settings.json");
        if (!AppPaths.IsPortable && !File.Exists(AppPaths.SettingsFile) && File.Exists(legacySettings)) { Directory.CreateDirectory(AppPaths.DataFolder); File.Copy(legacySettings, AppPaths.SettingsFile); }
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

    // Export: everything except passwords and this PC's player identity
    public string ExportJson()
    {
        var exported = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, jsonOptions), jsonOptions)!;
        exported.PlayerMac = "";
        foreach (var profile in exported.Profiles) profile.ProtectedPassword = "";
        return JsonSerializer.Serialize(exported, jsonOptions);
    }

    // Import keeps this PC's player identity and any saved password for a matching profile
    public static AppSettings Import(string importJson, AppSettings current)
    {
        var imported = JsonSerializer.Deserialize<AppSettings>(importJson, jsonOptions) ?? throw new InvalidDataException("Not a WinSqueeze settings file.");
        imported.PlayerMac = current.PlayerMac;
        foreach (var profile in imported.Profiles) profile.ProtectedPassword = current.Profiles.FirstOrDefault(existing => existing.Id == profile.Id)?.ProtectedPassword ?? "";
        imported.General.CloseToTrayTipShown = current.General.CloseToTrayTipShown;
        return imported;
    }

    // Locally administered unicast MAC (02:...)
    static string NewLocalMac()
    {
        var macBytes = RandomNumberGenerator.GetBytes(6);
        macBytes[0] = 0x02;
        return string.Join(":", macBytes.Select(macByte => macByte.ToString("x2")));
    }
}
