using System.Globalization;
using System.Text;
namespace WinSqueeze.Library;

// Lyrion escapes file paths in its URLs in the server's own filesystem encoding: UTF-8 on most servers, Latin-1 / Windows-1252 on some (#367: "Magnus %D6str%F6m" for "Magnus Öström")
public static class ServerPath
{
    static readonly UTF8Encoding strictUtf8 = new(false, true);
    static readonly Encoding westernEncoding;
    static ServerPath() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); westernEncoding = Encoding.GetEncoding(1252); }

    public static string Unescape(string escaped)
    {
        if (!escaped.Contains('%')) return escaped;
        var bytes = new List<byte>(escaped.Length);
        for (var index = 0; index < escaped.Length; index++)
        {
            if (escaped[index] == '%' && index + 2 < escaped.Length && byte.TryParse(escaped.AsSpan(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var escapedByte)) { bytes.Add(escapedByte); index += 2; continue; }
            var characterLength = char.IsHighSurrogate(escaped[index]) && index + 1 < escaped.Length ? 2 : 1;
            bytes.AddRange(Encoding.UTF8.GetBytes(escaped.Substring(index, characterLength))); index += characterLength - 1;
        }
        var rawBytes = bytes.ToArray();
        try { return strictUtf8.GetString(rawBytes); } catch (DecoderFallbackException) { return westernEncoding.GetString(rawBytes); }
    }

    // The path a file: URL points to, as the server sees it; any other URL comes back as it is
    public static string FromFileUrl(string url) => url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(url, UriKind.Absolute, out var fileUri) ? Unescape(fileUri.AbsolutePath) : url;
}
