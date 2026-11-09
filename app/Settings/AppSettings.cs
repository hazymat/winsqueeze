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
    public bool Exclusive { get; set; } // off by default: some drivers refuse exclusive mode; already-saved settings keep their choice
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
    public List<InfoChoice> Styles { get; set; } = new(); // the styles a click on the meters cycles through, in order (one ticked: a click taps the meter)
    public static readonly string[] StyleKeys = { "vu", "deck", "orange", "monitor", "blue", "green", "oledvu", "oled" };
    public static readonly string[] DefaultStyles = { "vu", "green", "oled" };
    // Fills in anything missing (first run, or a style added in a later version); at least one stays ticked
    public void Normalise()
    {
        Styles = Styles.Where(style => StyleKeys.Contains(style.Key)).GroupBy(style => style.Key).Select(group => group.First()).ToList();
        var firstRun = Styles.Count == 0;
        foreach (var styleKey in StyleKeys) if (Styles.All(style => style.Key != styleKey)) Styles.Add(new InfoChoice { Key = styleKey, On = firstRun ? DefaultStyles.Contains(styleKey) : false });
        if (firstRun) Styles = DefaultStyles.Select(styleKey => Styles.First(style => style.Key == styleKey)).Concat(Styles.Where(style => !DefaultStyles.Contains(style.Key))).ToList();
        if (Styles.All(style => !style.On)) Styles[0].On = true;
    }
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
    public double CutEarlierSeconds { get; set; } = 2; // stations and Lyrion announce a new song late; each split moves this far back
}

public class PlaybackSettings
{
    public string QueueEnd { get; set; } = "stop"; // stop | repeat (Lyrion's repeat all) | continue (Keep playing tops the queue up from the library)
    public string ContinueAdds { get; set; } = "album"; // album | tracks (about ten)
    public string ContinueFrom { get; set; } = "artist"; // artist (then genre, then anything) | genre (then anything) | anything
    public int ContinueAvoidDays { get; set; } = 3; // don't add the same album or track again within this many days
    public Dictionary<string, long> ContinueRecent { get; set; } = new(); // album key or "t<track id>" -> unix seconds when Keep playing added it
}

// Now playing's Artist, Album and Lyrics tabs beside Up next (SPEC.md › Now playing views)
public class InfoChoice { public string Key { get; set; } = ""; public bool On { get; set; } = true; }
public class TrackInfoSettings
{
    public List<InfoChoice> Tabs { get; set; } = new(); // order shown; unticked ones keep their place
    public Dictionary<string, List<InfoChoice>> Sources { get; set; } = new(); // per tab, tried from the top
    public bool Timeline { get; set; } = true; // the artist's music year by year, under the Artist text
    public bool Credits { get; set; } = true; // who played, wrote and produced the album, under the Album text
    public string ProtectedLastFmKey { get; set; } = ""; // DPAPI, current user only; never exported
    [JsonIgnore] public string LastFmKey
    {
        get { if (ProtectedLastFmKey == "") return ""; try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedLastFmKey), null, DataProtectionScope.CurrentUser)); } catch { return ""; } }
        set { ProtectedLastFmKey = value.Trim() == "" ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser)); }
    }
    public static readonly string[] TabKeys = { "artist", "album", "lyrics" };
    public static readonly Dictionary<string, string[]> SourceKeys = new() { ["artist"] = new[] { "plugin", "wikipedia", "lastfm" }, ["album"] = new[] { "plugin", "wikipedia", "lastfm" }, ["lyrics"] = new[] { "tags", "plugin", "lrclib" } };
    // Fills in anything missing (first run, or a source added in a later version), off for Last.fm until there's a key
    public void Normalise()
    {
        Tabs = Tabs.Where(tab => TabKeys.Contains(tab.Key)).GroupBy(tab => tab.Key).Select(group => group.First()).ToList();
        foreach (var tabKey in TabKeys) if (Tabs.All(tab => tab.Key != tabKey)) Tabs.Add(new InfoChoice { Key = tabKey });
        foreach (var (tabKey, sourceKeys) in SourceKeys)
        {
            var sources = (Sources.TryGetValue(tabKey, out var saved) ? saved : new()).Where(source => sourceKeys.Contains(source.Key)).GroupBy(source => source.Key).Select(group => group.First()).ToList();
            foreach (var sourceKey in sourceKeys) if (sources.All(source => source.Key != sourceKey)) sources.Add(new InfoChoice { Key = sourceKey, On = sourceKey != "lastfm" });
            Sources[tabKey] = sources;
        }
    }
}

public class LibrarySettings
{
    public string AlbumSource { get; set; } = "folders"; // folders | tags
    public string ContributorGrouping { get; set; } = "any"; // any | albumartist
    public string NameSeparators { get; set; } = ", ; & / feat. with and";
    public bool ReadSurnameFirst { get; set; } = true; // composer and conductor tags shaped "Kapustin, Nikolai" are one person, not two
    public string NameOrder { get; set; } = "surname"; // composers and conductors shown and sorted as surname | forename ("Kapustin, Nikolai" or "Nikolai Kapustin")
    public int RefreshMinutes { get; set; } = 5;
    public bool HideOwnComposers { get; set; } = true; // composers list leaves out songwriters credited only on their own albums
    public string ListSizeGraphic { get; set; } = "bar"; // top-level lists show size graphically: none | dots | bar | bold
    public string ListCounts { get; set; } = "group"; // top-level lists show: group (whatever the next view groups by) | tracks | both
    // Second-level browsing: groupings offered in each view's Group menu, and each area's starting grouping (SPEC.md › Browsing)
    public List<string> BrowseGroupings { get; set; } = new() { "artist", "year", "decade", "added", "format", "initial" };
    public Dictionary<string, string> DefaultGrouping { get; set; } = new() { ["genre"] = "artist", ["artist"] = "none", ["composer"] = "artist", ["performer"] = "artist", ["albums"] = "none" };
    // Custom views (SPEC.md › Custom views): the classical demos ship switched off
    public List<CustomView> CustomViews { get; set; } = CustomView.Demos();
}

// A browse menu of your own: limited to some genres (none = whole library), stepping through Levels (genre | composer | artist | performer), then albums
public class CustomView
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public string Group { get; set; } = ""; // views sharing a group sit in one sidebar entry of that name
    public List<string> Genres { get; set; } = new();
    public List<string> Levels { get; set; } = new();
    static readonly List<string> classicalGenres = new() { "Baroque", "Chamber Music", "Choral", "Classical", "Minimalist", "Opera", "Orchestral", "Piano Music", "String Music", "Symphony" };
    static CustomView Demo(string id, string name, params string[] levels) => new() { Id = id, Name = name, Group = "Classical", Genres = new(classicalGenres), Levels = levels.ToList() };
    public static List<CustomView> Demos() => new() { Demo("classical-composers", "Composers", "composer"), Demo("composers-by-genre", "Composers by genre", "genre", "composer"), Demo("genres-by-composer", "Genres by composer", "composer", "genre"), Demo("composers-by-artist", "Composers by artist", "artist", "composer") };
}

public class GeneralSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimised { get; set; }
    public bool ResumeOnStartup { get; set; } // carry on at startup only if the server was still playing to this player (off by default)
    public bool ResumeAfterUpdate { get; set; } = true; // a restart for an update or version switch carries on from the same spot (on by default)
    public bool CloseToTray { get; set; } // off: Close (X) quits. Was CloseHidesToTray (on by default), renamed so everyone gets the new default
    public bool CloseToTrayTipShown { get; set; }
    public OtherSqueezeliteAction OtherSqueezelite { get; set; } = OtherSqueezeliteAction.Ask;
    public bool AutoUpdate { get; set; } = true;
    public Dictionary<string, double[]> ModeSizes { get; set; } = new();
    public double[] LyricsBounds { get; set; } = Array.Empty<double>(); // the popped-out lyrics window: left, top, width, height
    public string UpdateChannel { get; set; } = "stable"; // stable | dev
    public bool Glass { get; set; } = true; // Windows 11 acrylic backdrop behind a see-through UI
    // No title bar or standard border by default; the UI draws its own drag strip and buttons (stored as ShowTitleBar so older "Frameless": false files get the new default)
    public bool ShowTitleBar { get; set; }
    public bool HideHints { get; set; } // the dismissable tips around the UI
    public List<string> DismissedHints { get; set; } = new();
    public bool GlobalHotkeys { get; set; } // Ctrl+Alt+Space / Left / Right / Up / Down work from any app (off by default)
    public bool TrackToasts { get; set; } // a notification on each new track (off by default)
    public Dictionary<string, double[]> WindowBounds { get; set; } = new(); // per monitor layout: left, top, width, height, maximised
    public double UiScale { get; set; } = 1; // main window zoom (0.75..1.5), set in Appearance; Ctrl+wheel zoom is off
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
    public TrackInfoSettings TrackInfo { get; set; } = new();
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
        settings.TrackInfo.Normalise(); settings.Meters.Normalise();
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
        exported.TrackInfo.ProtectedLastFmKey = "";
        return JsonSerializer.Serialize(exported, jsonOptions);
    }

    // Import keeps this PC's player identity and any saved password for a matching profile
    public static AppSettings Import(string importJson, AppSettings current)
    {
        var imported = JsonSerializer.Deserialize<AppSettings>(importJson, jsonOptions) ?? throw new InvalidDataException("Not a WinSqueeze settings file.");
        imported.PlayerMac = current.PlayerMac;
        foreach (var profile in imported.Profiles) profile.ProtectedPassword = current.Profiles.FirstOrDefault(existing => existing.Id == profile.Id)?.ProtectedPassword ?? "";
        imported.General.CloseToTrayTipShown = current.General.CloseToTrayTipShown;
        imported.TrackInfo.ProtectedLastFmKey = current.TrackInfo.ProtectedLastFmKey; imported.TrackInfo.Normalise(); imported.Meters.Normalise();
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
