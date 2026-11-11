using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using WinSqueeze.Settings;
namespace WinSqueeze.Shell;

// Transporter mode's big line in the real unit's font (SPEC.md › Transporter mode): Lyrion's own Graphics/standard.2.font.bmp.
// Not bundled: Slim Devices graphics aren't licensed for redistribution, so it is fetched on first use (Michael Herger's suggestion) from the user's
// own Lyrion server if it serves it, else from Lyrion's public source, and kept in the data folder. Without it the bundled dot font is used.
public static class TransporterFont
{
    const string fileName = "standard.2.font.bmp";
    static readonly string[] publicSources = { "public/9.1", "public/9.0", "public/8.5" };
    static readonly HttpClient fontClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    static Task<string>? loading;
    static string CachePath => Path.Combine(AppPaths.DataFolder, "Fonts", fileName);

    // {"top":8,"space":n,"glyphs":{"A":"hex,hex,…"}}: each glyph's columns as bitmasks of display rows 8 (bit 0) to 31, like ui/src/parts/vfdFont.ts; "null" when unavailable
    public static Task<string> GlyphsJsonAsync() { if (loading == null || loading.IsCompleted && loading.Result == "null") loading = LoadAsync(); return loading; } // a failed try (offline) is tried again next time

    static async Task<string> LoadAsync()
    {
        try
        {
            if (!File.Exists(CachePath)) await DownloadAsync();
            return File.Exists(CachePath) ? ToJson(File.ReadAllBytes(CachePath)) : "null";
        }
        catch (Exception error) { CrashReporter.Write("Transporter font not loaded", error, false); return "null"; }
    }

    static async Task DownloadAsync()
    {
        var sources = new List<string>();
        if (App.Settings.ActiveProfile is { Host.Length: > 0 } profile) sources.Add($"http://{profile.Host}:{profile.Port}/Graphics/{fileName}");
        foreach (var branch in publicSources) sources.Add($"https://raw.githubusercontent.com/LMS-Community/slimserver/{branch}/Graphics/{fileName}");
        foreach (var source in sources)
        {
            try
            {
                using var response = await fontClient.GetAsync(source);
                if (!response.IsSuccessStatusCode) continue;
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (ParseColumns(bytes) == null) continue; // a web page or anything else that isn't the font
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                File.WriteAllBytes(CachePath, bytes);
                return;
            }
            catch { } // offline or blocked: try the next source
        }
    }

    // A 1-bit BMP strip, as Lyrion reads it (Slim::Display::Lib::Fonts::parseFont): a dark dot on the bottom row separates characters,
    // the columns between are one character each, in Windows-1252 order; character 0 is the gap put between letters
    static List<List<uint>>? ParseColumns(byte[] bytes)
    {
        if (bytes.Length < 62 || bytes[0] != 'B' || bytes[1] != 'M') return null;
        int pixelOffset = BitConverter.ToInt32(bytes, 10), headerSize = BitConverter.ToInt32(bytes, 14), width = BitConverter.ToInt32(bytes, 18), height = BitConverter.ToInt32(bytes, 22);
        if (BitConverter.ToInt16(bytes, 28) != 1 || BitConverter.ToInt32(bytes, 30) != 0 || Math.Abs(height) != 33 || width < 100) return null;
        var paletteStart = 14 + headerSize;
        int Brightness(int entry) => bytes[paletteStart + entry * 4] + bytes[paletteStart + entry * 4 + 1] + bytes[paletteStart + entry * 4 + 2];
        var inkBit = Brightness(1) < Brightness(0) ? 1 : 0;
        int stride = (width + 31) / 32 * 4, rows = Math.Abs(height);
        if (pixelOffset + stride * rows > bytes.Length) return null;
        bool Ink(int column, int row) { var fileRow = height > 0 ? rows - 1 - row : row; return ((bytes[pixelOffset + fileRow * stride + column / 8] >> (7 - column % 8)) & 1) == inkBit; }
        var characters = new List<List<uint>>();
        for (var column = 0; column < width; column++)
        {
            if (Ink(column, rows - 1)) continue;
            var character = new List<uint>();
            for (; column < width && !Ink(column, rows - 1); column++) { uint mask = 0; for (var row = 8; row < 32; row++) if (Ink(column, row)) mask |= 1u << (row - 8); character.Add(mask); }
            characters.Add(character);
        }
        return characters.Count >= 256 ? characters : null;
    }

    static string ToJson(byte[] bytes)
    {
        var characters = ParseColumns(bytes);
        if (characters == null) return "null";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var windows1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var glyphs = new Dictionary<string, string>();
        for (var code = 33; code < 256; code++)
        {
            if (code is 127 or 160 || characters[code].All(mask => mask == 0)) continue;
            string character; try { character = windows1252.GetString(new[] { (byte)code }); } catch (DecoderFallbackException) { continue; }
            glyphs[character] = string.Join(",", characters[code].Select(mask => mask.ToString("x")));
        }
        var gap = characters[0].Any(mask => mask != 0) ? 0 : characters[0].Count; // dots in the gap character mean no gap
        return JsonSerializer.Serialize(new { top = 8, gap, space = characters[32].Count, glyphs });
    }
}
