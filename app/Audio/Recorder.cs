using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json;
using WinSqueeze.Server;
using WinSqueeze.Settings;
using WinSqueeze.Shell;
namespace WinSqueeze.Audio;

// Radio recording (SPEC.md › Recording): asks our Squeezelite to write what this PC's player plays to FLAC through
// "Local\squeezelite-rec-<mac>" (see vendor/squeezelite/src/output_rec_win.c), and starts a new file when the song changes.
public sealed class Recorder : IDisposable
{
    const int RequestOffset = 4, ActiveOffset = 8, AppliedOffset = 12, ErrorOffset = 16, PathOffset = 40, PathChars = 1024, TagsOffset = PathOffset + PathChars * 2, TagBytes = 4096, MapBytes = TagsOffset + TagBytes;
    MemoryMappedFile? sharedFile; MemoryMappedViewAccessor? view;
    string mapName = "";
    int request;
    readonly System.Threading.Timer timer;
    readonly object writeLock = new();
    string songKey = "", station = "", songTitle = "", songArtist = "";
    bool pausedForRestart;
    public bool Recording { get; private set; }
    public string CurrentFile { get; private set; } = "";
    public DateTime StartedAt { get; private set; }
    public int FilesThisRecording { get; private set; }
    string noteText = "";
    public string Note { get => noteText; private set { noteText = value; if (value != "") NoteId++; } } // last thing worth telling the user (saved, or an error)
    public int NoteId { get; private set; }
    public event Action? Changed;
    static RecordingSettings Tuning => App.Instance.Settings.Recording;
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "WinSqueeze recordings");
    public string Folder => Tuning.Folder.Trim() == "" ? DefaultFolder : Tuning.Folder.Trim();

    public Recorder() { timer = new System.Threading.Timer(_ => Tick(), null, 1000, 1000); }

    public void Start(string playerMac) { lock (writeLock) { pausedForRestart = false; mapName = "Local\\squeezelite-rec-" + playerMac.Replace(":", "").ToLowerInvariant(); view?.Dispose(); sharedFile?.Dispose(); view = null; sharedFile = null; } }

    // A stream (internet radio, or a plugin's station) rather than a file from the library. Not "no duration": BBC-style plugins give live programmes a length
    public static bool IsStream(JsonElement status) => status.ValueKind == JsonValueKind.Object && LyrionClient.ReadString(status, "remote") == "1";

    public void Toggle(JsonElement status) { if (Recording) Stop("Recording saved"); else Begin(status); }

    void Begin(JsonElement status)
    {
        if (!IsStream(status)) { Note = "Recording works on radio stations and other streams"; Changed?.Invoke(); return; }
        station = ""; // read once per recording: plugins later swap in the programme name, which would scatter files across folders
        ReadSong(status);
        lock (writeLock) { Recording = true; FilesThisRecording = 0; StartedAt = DateTime.Now; Note = ""; }
        NextFile();
    }

    public void Stop(string message)
    {
        lock (writeLock)
        {
            if (!Recording) return;
            Recording = false; Note = FilesThisRecording > 1 ? $"{message}: {FilesThisRecording} files in {Folder}" : $"{message} in {Folder}";
            WriteRequest(false, "", "");
        }
        Changed?.Invoke();
    }

    // Before Squeezelite restarts or the app quits: let it finish the file (closing it writes the FLAC header's length)
    public void Flush()
    {
        lock (writeLock)
        {
            if (!Recording || view == null) return;
            WriteRequest(false, "", "");
            for (var wait = 0; wait < 20 && view.ReadInt32(AppliedOffset) != request; wait++) Thread.Sleep(100);
            pausedForRestart = true; // carries on in a new file once Squeezelite is back (Start)
        }
    }

    // From each poll of the selected player: a new song starts a new file; leaving the stream ends the recording
    public void OnStatus(JsonElement status, bool ownPlayerSelected)
    {
        if (!Recording || !ownPlayerSelected || status.ValueKind != JsonValueKind.Object) return;
        if (!IsStream(status) || LyrionClient.ReadString(status, "mode") == "stop") { Stop("Recording saved"); return; }
        var previousKey = songKey;
        ReadSong(status);
        if (Tuning.SplitOnSongChange && songKey != previousKey) NextFile();
    }

    void ReadSong(JsonElement status)
    {
        var track = MediaControls.CurrentTrack(status) ?? default; // default reads as empty
        var title = LyrionClient.ReadString(track, "title"); var artist = LyrionClient.ReadString(track, "artist");
        var currentTitle = LyrionClient.ReadString(status, "current_title");
        var streamName = FirstOf(LyrionClient.ReadString(track, "remote_title"), LyrionClient.ReadString(status, "remote_title"), artist == "" && currentTitle != "" && currentTitle != title ? title : "", LyrionClient.ReadString(track, "album"), "Radio");
        if (station == "") station = streamName;
        // Plain ICY streams only send "Artist - Title" as the stream title; stations with a metadata plugin fill in title and artist
        if (title == station || title == streamName || title == "") { title = currentTitle; artist = ""; var dash = title.IndexOf(" - ", StringComparison.Ordinal); if (dash > 0) { artist = title[..dash]; title = title[(dash + 3)..]; } }
        songTitle = title; songArtist = artist; songKey = title + "\u0001" + artist;
    }

    void NextFile()
    {
        lock (writeLock)
        {
            if (!Recording) return;
            var folder = Path.Combine(Folder, Safe(station));
            try { Directory.CreateDirectory(folder); }
            catch (Exception error) { Recording = false; Note = $"Can't create the recordings folder: {error.Message}"; WriteRequest(false, "", ""); Changed?.Invoke(); return; }
            var song = songArtist != "" && songTitle != "" ? $"{songArtist} - {songTitle}" : FirstOf(songTitle, station);
            var stem = Path.Combine(folder, $"{DateTime.Now:yyyy-MM-dd HH.mm} {Safe(song, 80)}"); // programme blurbs can run to paragraphs
            if (stem.Length > 230) stem = stem[..230];
            var path = stem + ".flac";
            for (var copy = 2; File.Exists(path); copy++) path = $"{stem} ({copy}).flac";
            var tags = new StringBuilder();
            void Tag(string name, string value) { if (value != "") tags.Append(name).Append('=').Append(value.Replace('\n', ' ')).Append('\n'); }
            Tag("TITLE", FirstOf(songTitle, station)); Tag("ARTIST", songArtist); Tag("ALBUM", station); Tag("DATE", DateTime.Now.ToString("yyyy-MM-dd")); Tag("COMMENT", $"Recorded from {station} with WinSqueeze");
            CurrentFile = path; FilesThisRecording++;
            WriteRequest(true, path, tags.ToString());
        }
        Changed?.Invoke();
    }

    void WriteRequest(bool active, string path, string tags)
    {
        if (!OpenView()) return;
        var sequence = view!.ReadInt32(0); if ((sequence & 1) != 0) sequence++;
        view.Write(0, sequence + 1); // odd: Squeezelite ignores the block until it's even again
        var pathBytes = new byte[PathChars * 2]; Encoding.Unicode.GetBytes(path, 0, Math.Min(path.Length, PathChars - 1), pathBytes, 0);
        view.WriteArray(PathOffset, pathBytes, 0, pathBytes.Length);
        var tagBytes = new byte[TagBytes]; var encodedTags = Encoding.UTF8.GetBytes(tags); Array.Copy(encodedTags, tagBytes, Math.Min(encodedTags.Length, TagBytes - 1));
        view.WriteArray(TagsOffset, tagBytes, 0, tagBytes.Length);
        view.Write(ActiveOffset, active ? 1 : 0);
        request = Math.Max(request, view.ReadInt32(RequestOffset)) + 1; view.Write(RequestOffset, request);
        view.Write(0, sequence + 2);
    }

    bool OpenView()
    {
        if (view != null) return true;
        if (mapName == "") return false;
        try { sharedFile = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.ReadWrite); view = sharedFile.CreateViewAccessor(0, MapBytes, MemoryMappedFileAccess.ReadWrite); return true; }
        catch (FileNotFoundException) { return false; }
    }

    // Squeezelite restarted (device change, update) and cleared the block: carry on in a new file. Also reports write errors
    void Tick()
    {
        var changed = false;
        lock (writeLock)
        {
            try
            {
                if (!Recording || pausedForRestart || !OpenView()) return;
                if (view!.ReadInt32(RequestOffset) != request) { NextFile(); return; }
                var error = view.ReadInt32(ErrorOffset);
                if (view.ReadInt32(AppliedOffset) == request && error > 0)
                {
                    Recording = false; Note = error == 1 ? $"Can't write to {Path.GetDirectoryName(CurrentFile)}" : "Recording stopped: the FLAC encoder failed"; WriteRequest(false, "", ""); changed = true;
                }
            }
            catch (Exception error) { CrashReporter.Write("Recorder update failed", error, false); view?.Dispose(); sharedFile?.Dispose(); view = null; sharedFile = null; }
        }
        if (changed) Changed?.Invoke();
    }

    static string FirstOf(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    static string Safe(string name, int maxLength = 120) { var cleaned = string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim(); if (cleaned.Length > maxLength) cleaned = cleaned[..maxLength].TrimEnd() + "…"; cleaned = cleaned.TrimEnd('.', ' '); return cleaned == "" ? "Radio" : cleaned; }

    public void Dispose() { timer.Dispose(); Flush(); view?.Dispose(); sharedFile?.Dispose(); }
}
