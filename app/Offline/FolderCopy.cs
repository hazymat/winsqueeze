using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using NAudio.Wave;
using WinSqueeze.Settings;
namespace WinSqueeze.Offline;

// Copy to folder (SPEC.md › Copy to folder): a playlist, albums or tracks copied into a folder the user picks, such as a USB stick for the car, numbered in play order.
// As MP3 (most car stereos can't play FLAC) or the original files. A track downloaded for offline play is read from this PC, the rest come from Lyrion like a download.
// MP3 encoding is Windows' own (Media Foundation), so nothing extra ships; files already MP3 are copied as they are. One copy at a time
public static class FolderCopy
{
    static int running;
    public static bool Busy => running == 1;
    public record Progress(int Done, int Failed, int Total, string Current, bool Finished, string Problem);

    public static async Task RunAsync(List<Dictionary<string, object?>> tracks, string folder, bool asMp3, ServerProfile? profile, Func<long, OfflineTrack?> findDownloaded, Action<Progress> report)
    {
        if (Interlocked.Exchange(ref running, 1) == 1) return;
        int done = 0, failed = 0; var problem = "";
        var numberWidth = Math.Max(2, tracks.Count.ToString().Length);
        var tempFolder = Path.Combine(Path.GetTempPath(), "WinSqueeze-copy");
        try
        {
            Directory.CreateDirectory(folder);
            for (var position = 0; position < tracks.Count; position++)
            {
                var track = tracks[position];
                string Text(string name) => Convert.ToString(track.GetValueOrDefault(name)) ?? "";
                var trackId = Convert.ToInt64(track["id"]);
                var originalExtension = DownloadManager.Extension(Text("url"), Text("content_type"));
                var convert = asMp3 && originalExtension != ".mp3";
                var baseName = DownloadManager.SafeName((position + 1).ToString().PadLeft(numberWidth, '0') + " " + (Text("artist") == "" ? "" : Text("artist") + " - ") + Text("title"), Text("id"));
                var finalPath = Path.Combine(folder, baseName + (convert ? ".mp3" : originalExtension));
                report(new(done, failed, tracks.Count, Text("title"), false, problem));
                if (File.Exists(finalPath) && new FileInfo(finalPath).Length > 0) { done++; continue; } // copied last time
                string? tempSource = null;
                try
                {
                    var source = findDownloaded(trackId)?.Path is { } downloadedPath && File.Exists(downloadedPath) ? downloadedPath : null;
                    if (source == null)
                    {
                        if (profile is not { Host.Length: > 0 }) throw new IOException(L.T("Not connected to a server"));
                        Directory.CreateDirectory(tempFolder);
                        source = tempSource = convert ? Path.Combine(tempFolder, trackId + originalExtension) : finalPath + ".part";
                        await FetchAsync(profile, trackId, source);
                    }
                    if (convert) await Task.Run(() => EncodeMp3(source, finalPath + ".part", Text("title"), Text("artist"), Text("album_title"), position + 1, tracks.Count));
                    else if (source != tempSource) File.Copy(source, finalPath + ".part", true);
                    File.Move(finalPath + ".part", finalPath, true);
                    done++;
                }
                catch (Exception copyError)
                {
                    failed++; problem = copyError.Message;
                    try { File.Delete(finalPath + ".part"); } catch { }
                    Shell.CrashReporter.Write("Copy to folder: " + Text("title"), copyError, false);
                }
                finally { if (tempSource != null && convert) try { File.Delete(tempSource); } catch { } }
            }
        }
        catch (Exception folderError) { problem = folderError.Message; failed = tracks.Count - done; }
        finally { running = 0; report(new(done, failed, tracks.Count, "", true, problem)); }
    }

    static async Task FetchAsync(ServerProfile profile, long trackId, string destination)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        if (profile.Username != "") httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{profile.Username}:{profile.Password}")));
        using var response = await httpClient.GetAsync($"http://{profile.Host}:{profile.Port}/music/{trackId}/download", HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) throw new IOException(L.T("The server answered {status}", ("status", $"{(int)response.StatusCode} {response.ReasonPhrase}")));
        await using var file = File.Create(destination);
        await response.Content.CopyToAsync(file);
    }

    // 320 kbps stereo at 44.1 kHz (the encoder takes 16-bit at 32, 44.1 or 48 kHz, so hi-res and surround are converted first), then ID3 tags so the car shows the names
    static void EncodeMp3(string source, string destination, string title, string artist, string album, int trackNumber, int trackCount)
    {
        using (var reader = new MediaFoundationReader(source))
        using (var resampler = new MediaFoundationResampler(reader, new WaveFormat(44100, 16, 2)) { ResamplerQuality = 60 })
            MediaFoundationEncoder.EncodeToMp3(resampler, destination, 320000);
        var audio = File.ReadAllBytes(destination);
        using var output = File.Create(destination);
        output.Write(Id3Tag(title, artist, album, $"{trackNumber}/{trackCount}"));
        output.Write(audio, Id3Length(audio), audio.Length - Id3Length(audio)); // replaces any tag the encoder wrote
    }
    static int Id3Length(byte[] audio) => audio.Length > 10 && audio[0] == 'I' && audio[1] == 'D' && audio[2] == '3' ? 10 + (audio[6] << 21 | audio[7] << 14 | audio[8] << 7 | audio[9]) : 0;
    // ID3v2.3 with UTF-16 text frames: title, artist, album, track
    static byte[] Id3Tag(string title, string artist, string album, string track)
    {
        var frames = new MemoryStream();
        foreach (var (frameId, value) in new[] { ("TIT2", title), ("TPE1", artist), ("TALB", album), ("TRCK", track) })
        {
            if (value == "") continue;
            var text = new byte[] { 1 }.Concat(Encoding.Unicode.GetPreamble()).Concat(Encoding.Unicode.GetBytes(value)).ToArray();
            frames.Write(Encoding.ASCII.GetBytes(frameId)); frames.Write(new[] { (byte)(text.Length >> 24), (byte)(text.Length >> 16), (byte)(text.Length >> 8), (byte)text.Length, (byte)0, (byte)0 }); frames.Write(text);
        }
        var size = (int)frames.Length;
        return new byte[] { (byte)'I', (byte)'D', (byte)'3', 3, 0, 0, (byte)(size >> 21 & 0x7F), (byte)(size >> 14 & 0x7F), (byte)(size >> 7 & 0x7F), (byte)(size & 0x7F) }.Concat(frames.ToArray()).ToArray();
    }
}
