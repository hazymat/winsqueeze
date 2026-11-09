using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinSqueeze.Server;
namespace WinSqueeze.Shell;

// Artist, Album and Lyrics for Now playing (SPEC.md › Now playing views): each tab tries its ticked sources in order and shows the first that has something
public sealed class TrackInfo
{
    public record Request(string Kind, long TrackId, string ArtistId, string AlbumId, string Artist, string Album, string Title, double Duration, bool Remote);
    public record Result(bool Found, string Heading = "", string Subheading = "", string Text = "", string Image = "", string Source = "", string Link = "")
    { public object ToJson() => new { found = Found, heading = Heading, subheading = Subheading, text = Text, image = Image, source = Source, link = Link }; }

    static App App => App.Instance;
    static readonly HttpClient web = NewWebClient();
    readonly Dictionary<string, Task<Result>> lookups = new(); // finished and in-flight, so switching tabs and track changes never ask twice
    readonly SemaphoreSlim musicBrainzGate = new(1, 1); DateTime lastMusicBrainzCall = DateTime.MinValue; // MusicBrainz allows one request a second
    bool? pluginInstalled; string pluginCheckedFor = "";

    static HttpClient NewWebClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"WinSqueeze/{typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"} (+https://github.com/hazymat/winsqueeze)");
        return client;
    }

    public void Forget() { lock (lookups) lookups.Clear(); pluginInstalled = null; } // sources or key changed

    public Task<Result> LookupAsync(Request request)
    {
        var lookupKey = string.Join("|", request.Kind, request.Artist.ToLowerInvariant(), request.Kind == "artist" ? "" : request.Album.ToLowerInvariant(), request.Kind == "lyrics" ? request.Title.ToLowerInvariant() : "");
        lock (lookups) { if (!lookups.TryGetValue(lookupKey, out var lookup) || lookup.IsFaulted) lookups[lookupKey] = lookup = RunAsync(request); return lookup; }
    }

    // The Music & Artist Information plugin on the server (null while the server can't be asked)
    public async Task<bool?> PluginInstalledAsync()
    {
        var serverKey = App.Settings.ActiveProfile?.Id ?? "";
        if (pluginInstalled != null && pluginCheckedFor == serverKey) return pluginInstalled;
        if (App.IsOffline || App.Settings.ActiveProfile == null) return null;
        try { pluginInstalled = LyrionClient.ReadString(await App.ServerRequestAsync("", "can", "musicartistinfo", "biography", "?"), "_can") == "1"; pluginCheckedFor = serverKey; return pluginInstalled; }
        catch { return null; }
    }

    async Task<Result> RunAsync(Request request)
    {
        if (!App.Settings.TrackInfo.Sources.TryGetValue(request.Kind, out var sources) || (request.Artist == "" && request.Title == "")) return new(false);
        foreach (var source in sources.Where(source => source.On))
        {
            try
            {
                var result = (request.Kind, source.Key) switch
                {
                    ("lyrics", "tags") => await FromTagsAsync(request),
                    (_, "plugin") => await FromPluginAsync(request),
                    ("lyrics", "lrclib") => await FromLrclibAsync(request),
                    (_, "wikipedia") => await FromWikipediaAsync(request),
                    (_, "lastfm") => await FromLastFmAsync(request),
                    _ => null
                };
                if (result is { Found: true } && request.Kind != "lyrics") result = Tidy(request, result);
                if (result is { Found: true }) return result;
            }
            catch (Exception error) { CrashReporter.Note($"Track info {request.Kind} from {source.Key}: {error.Message}"); }
        }
        return new(false);
    }

    Result Found(Request request, string text, string source, string image = "", string link = "")
    {
        var heading = request.Kind switch { "artist" => request.Artist, "album" => request.Album, _ => request.Title };
        var subheading = request.Kind == "artist" ? "" : request.Artist;
        return new(true, heading, subheading, text.Trim(), image, source, link);
    }

    // ---- On the server ----
    async Task<Result?> FromTagsAsync(Request request)
    {
        if (request.TrackId <= 0 || App.IsOffline) return null;
        var songInfo = await App.ServerRequestAsync("", "songinfo", 0, 100, $"track_id:{request.TrackId}", "tags:W");
        if (!songInfo.TryGetProperty("songinfo_loop", out var fields)) return null;
        foreach (var field in fields.EnumerateArray()) if (LyrionClient.ReadString(field, "lyrics") is { Length: > 0 } lyrics) return Found(request, CleanLyrics(lyrics), "The file's own tags");
        return null;
    }

    async Task<Result?> FromPluginAsync(Request request)
    {
        if (App.IsOffline || await PluginInstalledAsync() != true) return null;
        var source = "The server's Music & Artist Information plugin";
        var parameters = new List<object> { "musicartistinfo" };
        switch (request.Kind)
        {
            case "artist":
                parameters.AddRange(new object[] { "biography", $"artist:{request.Artist}" }); if (request.ArtistId != "" && !request.Remote) parameters.Add($"artist_id:{request.ArtistId}");
                var biography = await App.ServerRequestAsync("", parameters.ToArray());
                if (LyrionClient.ReadString(biography, "biography") is not { Length: > 0 } biographyText || LyrionClient.ReadString(biography, "error") != "") return null;
                var photo = ""; try { photo = LyrionClient.ReadString(await App.ServerRequestAsync("", "musicartistinfo", "artistphoto", $"artist:{request.Artist}"), "url"); } catch { }
                return Found(request, HtmlToText(biographyText), source, photo);
            case "album":
                parameters.AddRange(new object[] { "albumreview", $"artist:{request.Artist}", $"album:{request.Album}" }); if (request.AlbumId != "" && !request.Remote) parameters.Add($"album_id:{request.AlbumId}");
                var review = await App.ServerRequestAsync("", parameters.ToArray());
                return LyrionClient.ReadString(review, "albumreview") is { Length: > 0 } reviewText && LyrionClient.ReadString(review, "error") == "" ? Found(request, HtmlToText(reviewText), source) : null;
            default:
                parameters.Add("lyrics");
                if (request.TrackId > 0) parameters.Add($"track_id:{request.TrackId}"); else parameters.AddRange(new object[] { $"artist:{request.Artist}", $"title:{request.Title}" });
                var lyrics = await App.ServerRequestAsync(request.Remote ? App.Session.PlayerId : "", parameters.ToArray());
                return LyrionClient.ReadString(lyrics, "lyrics") is { Length: > 0 } lyricsText && LyrionClient.ReadString(lyrics, "error") == "" ? Found(request, CleanLyrics(lyricsText), source) : null;
        }
    }

    // ---- Online: only the artist, album and track names are sent ----
    async Task<Result?> FromLrclibAsync(Request request)
    {
        if (request.Artist == "" || request.Title == "") return null;
        var address = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(request.Artist)}&track_name={Uri.EscapeDataString(request.Title)}" + (request.Album != "" ? $"&album_name={Uri.EscapeDataString(request.Album)}" : "") + (request.Duration > 0 ? $"&duration={Math.Round(request.Duration)}" : "");
        var record = await GetJsonAsync(address);
        if (record is not { ValueKind: JsonValueKind.Object }) // no exact match (album or length differ): the best search hit
        {
            var hits = await GetJsonAsync($"https://lrclib.net/api/search?artist_name={Uri.EscapeDataString(request.Artist)}&track_name={Uri.EscapeDataString(request.Title)}");
            record = hits is { ValueKind: JsonValueKind.Array } ? hits.Value.EnumerateArray().Select(hit => (JsonElement?)hit).FirstOrDefault(hit => LyrionClient.ReadString(hit!.Value, "plainLyrics") != "" && (request.Duration <= 0 || Math.Abs(LyrionClient.ReadDouble(hit!.Value, "duration") - request.Duration) < 5)) : null;
        }
        if (record is not { } found) return null;
        if (found.TryGetProperty("instrumental", out var instrumental) && instrumental.ValueKind == JsonValueKind.True) return Found(request, "Instrumental", "LRCLIB", link: "https://lrclib.net");
        return LyrionClient.ReadString(found, "plainLyrics") is { Length: > 0 } plainLyrics ? Found(request, CleanLyrics(plainLyrics), "LRCLIB", link: "https://lrclib.net") : null;
    }

    async Task<Result?> FromWikipediaAsync(Request request)
    {
        if (request.Artist == "" || (request.Kind == "album" && request.Album == "")) return null;
        // MusicBrainz finds the right entity, its Wikidata link names the Wikipedia page, and Wikipedia gives the introduction
        var search = request.Kind == "artist" ? $"https://musicbrainz.org/ws/2/artist/?query={Uri.EscapeDataString($"artist:\"{LuceneEscape(request.Artist)}\"")}&fmt=json&limit=5" : $"https://musicbrainz.org/ws/2/release-group/?query={Uri.EscapeDataString($"releasegroup:\"{LuceneEscape(request.Album)}\" AND artist:\"{LuceneEscape(request.Artist)}\"")}&fmt=json&limit=5";
        var found = await MusicBrainzAsync(search);
        var listName = request.Kind == "artist" ? "artists" : "release-groups";
        if (found is not { } results || !results.TryGetProperty(listName, out var matches)) return null;
        var wanted = Normalise(request.Kind == "artist" ? request.Artist : request.Album);
        var best = matches.EnumerateArray().FirstOrDefault(match => LyrionClient.ReadInt(match, "score") >= 90 && Normalise(LyrionClient.ReadString(match, request.Kind == "artist" ? "name" : "title")) == wanted);
        if (best.ValueKind != JsonValueKind.Object) return null;
        var entity = await MusicBrainzAsync($"https://musicbrainz.org/ws/2/{(request.Kind == "artist" ? "artist" : "release-group")}/{LyrionClient.ReadString(best, "id")}?inc=url-rels&fmt=json");
        if (entity is not { } details || !details.TryGetProperty("relations", out var relations)) return null;
        var pageTitle = "";
        foreach (var relation in relations.EnumerateArray())
        {
            var resource = relation.TryGetProperty("url", out var url) ? LyrionClient.ReadString(url, "resource") : "";
            if (LyrionClient.ReadString(relation, "type") == "wikipedia" && resource.Contains("en.wikipedia.org/wiki/")) { pageTitle = Uri.UnescapeDataString(resource[(resource.IndexOf("/wiki/") + 6)..]); break; }
            if (LyrionClient.ReadString(relation, "type") == "wikidata" && pageTitle == "" && await GetJsonAsync($"https://www.wikidata.org/wiki/Special:EntityData/{resource.Split('/').Last()}.json") is { } wikidata
                && wikidata.TryGetProperty("entities", out var entities)) foreach (var item in entities.EnumerateObject()) if (item.Value.TryGetProperty("sitelinks", out var sitelinks) && sitelinks.TryGetProperty("enwiki", out var enwiki)) pageTitle = LyrionClient.ReadString(enwiki, "title");
        }
        if (pageTitle == "") return null;
        var page = await GetJsonAsync($"https://en.wikipedia.org/w/api.php?action=query&prop=extracts|pageimages|pageprops&ppprop=disambiguation&exintro=1&explaintext=1&piprop=thumbnail&pithumbsize=400&redirects=1&format=json&titles={Uri.EscapeDataString(pageTitle)}");
        if (page is not { } pageResult || !pageResult.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return null;
        foreach (var wikiPage in pages.EnumerateObject())
        {
            var extract = LyrionClient.ReadString(wikiPage.Value, "extract");
            if (extract == "" || wikiPage.Value.TryGetProperty("pageprops", out var pageProps) && pageProps.TryGetProperty("disambiguation", out _)) continue;
            var image = wikiPage.Value.TryGetProperty("thumbnail", out var thumbnail) ? LyrionClient.ReadString(thumbnail, "source") : "";
            return Found(request, extract, "Wikipedia", request.Kind == "artist" ? image : "", $"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(LyrionClient.ReadString(wikiPage.Value, "title").Replace(' ', '_'))}");
        }
        return null;
    }

    async Task<Result?> FromLastFmAsync(Request request)
    {
        var apiKey = App.Settings.TrackInfo.LastFmKey;
        if (apiKey == "" || request.Artist == "" || (request.Kind == "album" && request.Album == "")) return null;
        var method = request.Kind == "artist" ? $"artist.getinfo&artist={Uri.EscapeDataString(request.Artist)}" : $"album.getinfo&artist={Uri.EscapeDataString(request.Artist)}&album={Uri.EscapeDataString(request.Album)}";
        var answer = await GetJsonAsync($"https://ws.audioscrobbler.com/2.0/?method={method}&autocorrect=1&api_key={Uri.EscapeDataString(apiKey)}&format=json");
        if (answer is not { } found || !found.TryGetProperty(request.Kind, out var entity)) return null;
        var block = request.Kind == "artist" ? "bio" : "wiki";
        if (!entity.TryGetProperty(block, out var wiki)) return null;
        var content = LyrionClient.ReadString(wiki, "content");
        content = Regex.Replace(content, @"<a href=""[^""]*"">Read more on Last\.fm</a>.*$", "", RegexOptions.Singleline); // the closing link and licence line
        content = Regex.Replace(HtmlToText(content), @"User-contributed text is available under.*$", "", RegexOptions.Singleline).Trim();
        return content == "" ? null : Found(request, content, "Last.fm", link: LyrionClient.ReadString(entity, "url"));
    }

    async Task<JsonElement?> MusicBrainzAsync(string address)
    {
        await musicBrainzGate.WaitAsync();
        try { var wait = lastMusicBrainzCall.AddMilliseconds(1100) - DateTime.UtcNow; if (wait > TimeSpan.Zero) await Task.Delay(wait); lastMusicBrainzCall = DateTime.UtcNow; return await GetJsonAsync(address); }
        finally { musicBrainzGate.Release(); }
    }

    static async Task<JsonElement?> GetJsonAsync(string address)
    {
        using var response = await web.GetAsync(address);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    // ---- Text ----
    // Artist and album text: never a "may refer to" list, never text that doesn't name the artist (a loose title match), and tidied into blocks the UI lays out:
    // paragraphs separated by a blank line, "## " headings, "• " list lines. Reference and link sections are dropped
    static Result Tidy(Request request, Result result)
    {
        if (Regex.IsMatch(result.Text, @"\bmay (also )?refer to\b|\bmight (also )?refer to\b", RegexOptions.IgnoreCase)) return new(false);
        var firstArtist = Regex.Split(request.Artist, @"\s*(?:[,;&/]|\bfeat\.?|\bfeaturing\b|\band\b|\bwith\b)\s*", RegexOptions.IgnoreCase).FirstOrDefault(name => name.Trim() != "") ?? request.Artist;
        var artistKey = Normalise(Regex.Replace(firstArtist, @"^the\s+", "", RegexOptions.IgnoreCase));
        if (artistKey.Length >= 3 && !Normalise(result.Text).Contains(artistKey)) return new(false);
        var text = Prose(result.Text);
        return text == "" ? new(false) : result with { Text = text };
    }

    static string Prose(string source)
    {
        var text = Regex.Replace(source.Replace("\r\n", "\n").Replace('\r', '\n'), @"\[([^\]]+)\]\([^)]*\)", "$1"); // markdown links keep their words
        text = Regex.Replace(text, @"(\*\*|__)(.+?)\1", "$2"); text = Regex.Replace(text, @"(?<![\w*])[*_]([^*_\n]+)[*_](?![\w*])", "$1");
        var lines = text.Split('\n'); var blocks = new List<string>(); var paragraph = new List<string>(); var list = new List<string>();
        void Flush() { if (paragraph.Count > 0) blocks.Add(string.Join(" ", paragraph)); if (list.Count > 0) blocks.Add(string.Join("\n", list)); paragraph.Clear(); list.Clear(); }
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim().TrimStart('>').Trim();
            var next = index + 1 < lines.Length ? lines[index + 1].Trim() : "";
            string heading = "";
            if (line != "" && Regex.IsMatch(next, @"^[-=]{3,}$")) { heading = line; index++; } // underlined heading
            else if (Regex.Match(line, @"^#{1,6}\s+(.+?)\s*#*$") is { Success: true } hashHeading) heading = hashHeading.Groups[1].Value;
            else if (Regex.Match(line, @"^==+\s*(.+?)\s*==+$") is { Success: true } wikiHeading) heading = wikiHeading.Groups[1].Value;
            if (heading != "")
            {
                Flush();
                if (Regex.IsMatch(heading, @"^(references|external links|see also|notes|sources|further reading|track listing|personnel)$", RegexOptions.IgnoreCase)) break; // the rest is lists of links and credits
                blocks.Add("## " + heading); continue;
            }
            if (line == "" || Regex.IsMatch(line, @"^[-=_*]{3,}$")) { Flush(); continue; }
            if (Regex.Match(line, @"^[*\-+•]\s+(.+)$") is { Success: true } bullet) { if (paragraph.Count > 0) Flush(); list.Add("• " + bullet.Groups[1].Value); continue; }
            if (list.Count > 0) Flush();
            paragraph.Add(line);
        }
        Flush();
        while (blocks.Count > 0 && blocks[^1].StartsWith("## ")) blocks.RemoveAt(blocks.Count - 1); // a heading with nothing under it
        return string.Join("\n\n", blocks).Trim();
    }

    static string LuceneEscape(string text) => Regex.Replace(text, @"([+\-&|!(){}\[\]^""~*?:\\/])", @"\$1");
    static string Normalise(string name) => Regex.Replace(name.ToLowerInvariant().Replace("&", "and"), @"[^\p{L}\p{N}]+", "");
    static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</p>|</div>|</h\d>", "\n\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<link[^>]*>|<style.*?</style>|<script.*?</script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", ""));
        return Regex.Replace(text.Replace("\r\n", "\n"), @"\n{3,}", "\n\n").Trim();
    }
    static string CleanLyrics(string lyrics) => Regex.Replace(lyrics.Replace("\r\n", "\n").Replace('\r', '\n'), @"^(\[\d+:\d+(?:[.:]\d+)?\])+\s?", "", RegexOptions.Multiline).Trim(); // timestamps from synced (LRC) lyrics
}
