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

// Meter tuning (Settings › Meters), applied live
public class MeterSettings
{
    public bool AutoReference { get; set; } = true; // 0 VU follows the music's loudness
    public bool FollowEq { get; set; } = true; // meters show the sound after the EQ
    public bool FollowVolume { get; set; } // meters drop with WinSqueeze's volume (off: they show the music at full level)
    public double ReferenceDbfs { get; set; } = -18; // RMS level that reads 0 VU when not automatic
    public double VuIntegrationMs { get; set; } = 300; // needle ballistics
    public double SpectrumFallMs { get; set; } = 250;
}

// Graphic equaliser and its window (SPEC.md › Equaliser)
public class EqualiserSettings
{
    public bool Enabled { get; set; } = false; // off = bit-perfect
    public int BandCount { get; set; } = 10; // 10, 7 or 3
    public string Character { get; set; } = "vintage"; // "vintage" (proportional Q, like the analogue units) or "precise" (constant Q)
    public bool Linked { get; set; } = true; // one curve for both channels
    public bool AutoPreamp { get; set; } = false; // AUTO lowers the preamp as bands rise (never clips, but boosts sound quieter overall)
    public double PreampDb { get; set; } = -6; // fixed headroom while on; boosts beyond it meet the safety limiter, so turning a band up never turns the rest down
    public Dictionary<string, double[]> Gains { get; set; } = new(); // "10L", "10R", "7L"... in dB
    public string Display { get; set; } = "sum"; // analyser: "sum" (L+R) or "split" (L/R)
    public string Decay { get; set; } = "fast";
    public string EditMode { get; set; } = "draw"; // "draw" sweeps across faders, "single" moves one
    public double? WindowLeft { get; set; } // null until the window has been placed (NaN can't be written as JSON)
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 330;
    public string DockSide { get; set; } = ""; // "", "left", "right", "top", "bottom" of the main window
    public int DockOffset { get; set; } // pixels along that edge
}

// Radio recording (SPEC.md › Recording)
public class RecordingSettings
{
    public bool ShowButton { get; set; } = true; // the record dot beside a playing radio station
    public string Folder { get; set; } = ""; // empty = Music\WinSqueeze recordings
    public bool SplitOnSongChange { get; set; } = true; // a new file for each song the station announces
    public bool FolderPerStation { get; set; } // false (default): a folder per recording; true: one folder per station, files dated
}

public class PlaybackSettings
{
    public string QueueEnd { get; set; } = "stop"; // stop | repeat (Lyrion's repeat all) | continue (Keep playing tops the queue up from the library)
    public string ContinueAdds { get; set; } = "album"; // album | tracks (about ten)
    public string ContinueFrom { get; set; } = "artist"; // artist (then genre, then anything) | genre (then anything) | anything
    public int ContinueAvoidDays { get; set; } = 3; // don't add the same album or track again within this many days
    public Dictionary<string, long> ContinueRecent { get; set; } = new(); // album key or "t<track id>" -> unix seconds when Keep playing added it
}

public class LibrarySettings
{
    public string AlbumSource { get; set; } = "folders"; // folders | tags
    public string ContributorGrouping { get; set; } = "any"; // any | albumartist
    public string NameSeparators { get; set; } = ", ; & / feat. with and";
    public int RefreshMinutes { get; set; } = 5;
    public bool HideOwnComposers { get; set; } = true; // composers list leaves out songwriters credited only on their own albums
    public string ListCounts { get; set; } = "group"; // top-level lists show: group (whatever the next view groups by) | tracks | both
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
    public string UpdateChannel { get; set; } = "stable"; // stable | dev
    public bool Glass { get; set; } = true; // Windows 11 acrylic backdrop behind a see-through UI
    // No title bar or standard border by default; the UI draws its own drag strip and buttons (stored as ShowTitleBar so older "Frameless": false files get the new default)
    public bool ShowTitleBar { get; set; }
    public bool HideHints { get; set; } // the dismissable tips around the UI
    public List<string> DismissedHints { get; set; } = new();
    public bool GlobalHotkeys { get; set; } // Ctrl+Alt+Space / Left / Right / Up / Down work from any app (off by default)
    public bool TrackToasts { get; set; } // a notification on each new track (off by default)
    public Dictionary<string, double[]> WindowBounds { get; set; } = new(); // per monitor layout: left, top, width, height, maximised
    public string Skin { get; set; } = "auto";
    public string AccentColour { get; set; } = ""; // #rrggbb chosen in Appearance; empty = the skin's own (blue)
    public int GlassTransparency { get; set; } = -1; // 0..100 % see-through for the glass tint; -1 = the skin's default
    public bool GlassWhenInactive { get; set; } = true; // keep the frosted look when another app has focus (Windows normally turns it solid)
    public int GlassTintHue { get; set; } = 0; // 0 = neutral, 1..360 = colour the glass
    public bool CoverBackdrop { get; set; } = true; // Now playing's blurred album-art colours behind the page
    public bool CoverAccent { get; set; } // accent colour taken from the playing cover (off by default) // auto | dark | light | classic | amber (SPEC.md › Skins)
    [JsonIgnore] public bool Frameless { get => !ShowTitleBar; set => ShowTitleBar = !value; }
    public string LastRunVersion { get; set; } = "";
}

public enum OtherSqueezeliteAction { Ask, Stop, Leave }

public class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    public MeterSettings Meters { get; set; } = new();
    public EqualiserSettings Equaliser { get; set; } = new();
    public LibrarySettings Library { get; set; } = new();
    public RecordingSettings Recording { get; set; } = new();
    public PlaybackSettings Playback { get; set; } = new();
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
