using System.Net;
using System.Security.Cryptography;
using System.Text;
using WinSqueeze.Settings;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinSqueeze.Server;
namespace WinSqueeze.Shell;

// Artist, Album and Lyrics for Now playing (SPEC.md › Now playing views): each tab tries its ticked sources in order and shows the first that has something
public sealed class TrackInfo
{
    public record Request(string Kind, long TrackId, string ArtistId, string AlbumId, string Artist, string Album, string Title, double Duration, bool Remote);
    public record Result(bool Found, string Heading = "", string Subheading = "", string Text = "", string Image = "", string Source = "", string Link = "", object? Events = null, bool Incomplete = false)
    { public object ToJson() => new { found = Found, heading = Heading, subheading = Subheading, text = Text, image = Image, source = Source, link = Link, events = Events }; }

    public static Request RequestFrom(JsonElement parameters)
    {
        string Text(string name) => parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText() : "";
        return new(Text("kind"), long.TryParse(Text("id"), out var trackId) ? trackId : 0, Text("artistId"), Text("albumId"), Text("artist"), Text("album"), Text("title"), double.TryParse(Text("duration"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var duration) ? duration : 0, Text("remote") == "1");
    }

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

    public void Forget() { lock (lookups) lookups.Clear(); lock (musicBrainzEntities) musicBrainzEntities.Clear(); pluginInstalled = null; } // sources or key changed

    public Task<Result> LookupAsync(Request request)
    {
        var lookupKey = string.Join("|", request.Kind, request.Artist.ToLowerInvariant(), request.Kind == "artist" ? "" : request.Album.ToLowerInvariant(), request.Kind == "lyrics" ? request.Title.ToLowerInvariant() : "");
        lock (lookups) { if (!lookups.TryGetValue(lookupKey, out var lookup) || lookup.IsFaulted || lookup.IsCompletedSuccessfully && lookup.Result.Incomplete) lookups[lookupKey] = lookup = RunAsync(request); return lookup; } // a lookup cut short by a busy source is asked again next time
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
        if (request.Kind == "timeline") return App.Settings.TrackInfo.Timeline ? await TimelineAsync(request) : new(false);
        if (request.Kind == "credits") return App.Settings.TrackInfo.Credits ? await CreditsAsync(request) : new(false);
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
        foreach (var field in fields.EnumerateArray()) if (LyrionClient.ReadString(field, "lyrics") is { Length: > 0 } lyrics) return LyricsFound(request, lyrics, L.T("The file's own tags"));
        return null;
    }

    async Task<Result?> FromPluginAsync(Request request)
    {
        if (App.IsOffline || await PluginInstalledAsync() != true) return null;
        var source = L.T("The server's Music & Artist Information plugin");
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
                return LyrionClient.ReadString(lyrics, "lyrics") is { Length: > 0 } lyricsText && LyrionClient.ReadString(lyrics, "error") == "" ? LyricsFound(request, lyricsText, source) : null;
        }
    }

    // ---- Online: only the artist, album and track names are sent ----
    async Task<Result?> FromLrclibAsync(Request request)
    {
        if (request.Artist == "" || request.Title == "") return null;
        var address = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(request.Artist)}&track_name={Uri.EscapeDataString(request.Title)}" + (request.Album != "" ? $"&album_name={Uri.EscapeDataString(request.Album)}" : "") + (request.Duration > 0 ? $"&duration={Math.Round(request.Duration)}" : "");
        var record = await GetJsonAsync(address);
        var exact = record is { ValueKind: JsonValueKind.Object } ? record : null;
        if (exact is { } exactRecord && (Instrumental(exactRecord) || LyrionClient.ReadString(exactRecord, "syncedLyrics") != "")) return LrclibResult(request, exactRecord);
        // No exact match (album or length differ) or it isn't timed: the best search hit of the right length, timed ones first
        var hits = await GetJsonAsync($"https://lrclib.net/api/search?artist_name={Uri.EscapeDataString(request.Artist)}&track_name={Uri.EscapeDataString(request.Title)}");
        var fitting = hits is { ValueKind: JsonValueKind.Array } ? hits.Value.EnumerateArray().Where(hit => LyrionClient.ReadString(hit, "plainLyrics") != "" && (request.Duration <= 0 || Math.Abs(LyrionClient.ReadDouble(hit, "duration") - request.Duration) < 5)).ToList() : new List<JsonElement>();
        var timed = fitting.Where(hit => LyrionClient.ReadString(hit, "syncedLyrics") != "").Select(hit => (JsonElement?)hit).FirstOrDefault();
        var found = timed ?? exact ?? fitting.Select(hit => (JsonElement?)hit).FirstOrDefault();
        return found is { } chosen ? LrclibResult(request, chosen) : null;
    }
    static bool Instrumental(JsonElement record) => record.TryGetProperty("instrumental", out var instrumental) && instrumental.ValueKind == JsonValueKind.True;
    Result? LrclibResult(Request request, JsonElement record)
    {
        if (Instrumental(record)) return Found(request, L.T("Instrumental"), "LRCLIB", link: "https://lrclib.net");
        string synced = LyrionClient.ReadString(record, "syncedLyrics"), plain = LyrionClient.ReadString(record, "plainLyrics");
        return plain != "" || synced != "" ? LyricsFound(request, plain != "" ? plain : synced, "LRCLIB", "https://lrclib.net", synced) : null;
    }

    async Task<Result?> FromWikipediaAsync(Request request)
    {
        if (request.Artist == "" || (request.Kind == "album" && request.Album == "")) return null;
        // MusicBrainz finds the right entity, its Wikidata link names the Wikipedia page, and Wikipedia gives the introduction: in the app's language when that page exists, else English
        var entity = await MusicBrainzEntityAsync(request.Kind, request.Artist, request.Album);
        if (entity is not { } details || !details.TryGetProperty("relations", out var relations)) return null;
        string pageTitle = "", pageLanguage = "en";
        foreach (var relation in relations.EnumerateArray())
        {
            var resource = relation.TryGetProperty("url", out var url) ? LyrionClient.ReadString(url, "resource") : "";
            if (LyrionClient.ReadString(relation, "type") == "wikipedia" && resource.Contains("en.wikipedia.org/wiki/") && pageTitle == "") { pageTitle = Uri.UnescapeDataString(resource[(resource.IndexOf("/wiki/") + 6)..]); if (L.Code == "en") break; }
            if (LyrionClient.ReadString(relation, "type") == "wikidata" && (pageTitle == "" || L.Code != "en") && await GetJsonAsync($"https://www.wikidata.org/wiki/Special:EntityData/{resource.Split('/').Last()}.json") is { } wikidata && wikidata.TryGetProperty("entities", out var entities))
                foreach (var item in entities.EnumerateObject())
                {
                    if (!item.Value.TryGetProperty("sitelinks", out var sitelinks)) continue;
                    if (L.Code != "en" && sitelinks.TryGetProperty(L.Code + "wiki", out var localWiki)) { pageTitle = LyrionClient.ReadString(localWiki, "title"); pageLanguage = L.Code; }
                    else if (pageTitle == "" && sitelinks.TryGetProperty("enwiki", out var enwiki)) pageTitle = LyrionClient.ReadString(enwiki, "title");
                }
            if (pageLanguage != "en") break;
        }
        if (pageTitle == "") return null;
        var page = await GetJsonAsync($"https://{pageLanguage}.wikipedia.org/w/api.php?action=query&prop=extracts|pageimages|pageprops&ppprop=disambiguation&exintro=1&explaintext=1&piprop=thumbnail&pithumbsize=400&redirects=1&format=json&titles={Uri.EscapeDataString(pageTitle)}");
        if (page is not { } pageResult || !pageResult.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return null;
        foreach (var wikiPage in pages.EnumerateObject())
        {
            var extract = LyrionClient.ReadString(wikiPage.Value, "extract");
            if (extract == "" || wikiPage.Value.TryGetProperty("pageprops", out var pageProps) && pageProps.TryGetProperty("disambiguation", out _)) continue;
            var image = wikiPage.Value.TryGetProperty("thumbnail", out var thumbnail) ? LyrionClient.ReadString(thumbnail, "source") : "";
            return Found(request, extract, "Wikipedia", request.Kind == "artist" ? image : "", $"https://{pageLanguage}.wikipedia.org/wiki/{Uri.EscapeDataString(LyrionClient.ReadString(wikiPage.Value, "title").Replace(' ', '_'))}");
        }
        return null;
    }

    // The artist or album on MusicBrainz: an exact name match scoring 90+, looked up with its links (and, for artists, life span and area); kept for the session
    readonly Dictionary<string, Task<JsonElement?>> musicBrainzEntities = new();
    Task<JsonElement?> MusicBrainzEntityAsync(string kind, string artist, string album)
    {
        var entityKey = $"{kind}|{artist.ToLowerInvariant()}|{(kind == "artist" ? "" : album.ToLowerInvariant())}";
        lock (musicBrainzEntities) { if (!musicBrainzEntities.TryGetValue(entityKey, out var lookup) || lookup.IsFaulted) musicBrainzEntities[entityKey] = lookup = FindAsync(); return lookup; }
        async Task<JsonElement?> FindAsync()
        {
            var search = kind == "artist" ? $"https://musicbrainz.org/ws/2/artist/?query={Uri.EscapeDataString($"artist:\"{LuceneEscape(artist)}\"")}&fmt=json&limit=5" : $"https://musicbrainz.org/ws/2/release-group/?query={Uri.EscapeDataString($"releasegroup:\"{LuceneEscape(album)}\" AND artist:\"{LuceneEscape(artist)}\"")}&fmt=json&limit=5";
            if (await MusicBrainzAsync(search) is not { } results || !results.TryGetProperty(kind == "artist" ? "artists" : "release-groups", out var matches)) return null;
            var wanted = Normalise(kind == "artist" ? artist : album);
            var best = matches.EnumerateArray().FirstOrDefault(match => LyrionClient.ReadInt(match, "score") >= 90 && Normalise(LyrionClient.ReadString(match, kind == "artist" ? "name" : "title")) == wanted);
            if (best.ValueKind != JsonValueKind.Object) return null;
            return await MusicBrainzAsync($"https://musicbrainz.org/ws/2/{(kind == "artist" ? "artist" : "release-group")}/{LyrionClient.ReadString(best, "id")}?inc=url-rels&fmt=json");
        }
    }

    // ---- Timeline: the artist's music, year by year (SPEC.md › Now playing views) ----
    // MusicBrainz: start and end (born/formed, died/split) and albums; Wikidata: bands joined, labels signed to, awards and nominations, only statements with a real source
    static readonly string[] skippedAlbumTypes = { "Compilation", "Remix", "DJ-mix", "Mixtape/Street", "Demo", "Interview", "Audiobook", "Audio drama", "Spokenword" };
    async Task<Result> TimelineAsync(Request request)
    {
        if (request.Artist == "" || await MusicBrainzEntityAsync("artist", request.Artist, "") is not { } artist) return new(false);
        var events = new List<Dictionary<string, object>>();
        void Add(int year, string kind, string title, string detail = "", string id = "", int endYear = 0) { if (year > 0) events.Add(new() { ["year"] = year, ["kind"] = kind, ["title"] = title, ["detail"] = detail, ["id"] = id, ["endYear"] = endYear }); }
        static int YearOf(string date) => date.Length >= 4 && int.TryParse(date[..4], out var year) ? year : 0;
        var isPerson = LyrionClient.ReadString(artist, "type") == "Person";
        var lifeSpan = artist.TryGetProperty("life-span", out var span) ? span : default;
        var beginArea = artist.TryGetProperty("begin-area", out var area) && area.ValueKind == JsonValueKind.Object ? LyrionClient.ReadString(area, "name") : "";
        Add(YearOf(LyrionClient.ReadString(lifeSpan, "begin")), "start", isPerson ? L.T("Born") : L.T("Formed"), beginArea);
        Add(YearOf(LyrionClient.ReadString(lifeSpan, "end")), "end", isPerson ? L.T("Died") : L.T("Split"));
        var albumsIncomplete = false;
        try
        {
            // Every album and EP, a page of 100 at a time: big discographies (Herbie Hancock has hundreds of release groups) run to several pages
            for (int offset = 0, total = 1; offset < total && offset < 1000; offset += 100)
            {
                if (await MusicBrainzAsync($"https://musicbrainz.org/ws/2/release-group?artist={LyrionClient.ReadString(artist, "id")}&type=album|ep&limit=100&offset={offset}&fmt=json") is not { } browse || !browse.TryGetProperty("release-groups", out var releaseGroups)) break;
                total = LyrionClient.ReadInt(browse, "release-group-count");
                foreach (var releaseGroup in releaseGroups.EnumerateArray())
                {
                    var secondary = releaseGroup.TryGetProperty("secondary-types", out var types) ? types.EnumerateArray().Select(type => type.GetString() ?? "").ToList() : new();
                    if (secondary.Any(type => skippedAlbumTypes.Contains(type))) continue;
                    var detail = secondary.Contains("Live") ? L.T("Live album") : secondary.Contains("Soundtrack") ? L.T("Soundtrack") : LyrionClient.ReadString(releaseGroup, "primary-type") == "EP" ? "EP" : "";
                    Add(YearOf(LyrionClient.ReadString(releaseGroup, "first-release-date")), "album", LyrionClient.ReadString(releaseGroup, "title"), detail, LyrionClient.ReadString(releaseGroup, "id"));
                }
                if (releaseGroups.GetArrayLength() == 0) break;
            }
        }
        catch (Exception error) { CrashReporter.Note("Timeline albums: " + error.Message); albumsIncomplete = true; }
        var wikidataId = artist.TryGetProperty("relations", out var relations) ? relations.EnumerateArray().Where(relation => LyrionClient.ReadString(relation, "type") == "wikidata").Select(relation => relation.TryGetProperty("url", out var url) ? LyrionClient.ReadString(url, "resource").Split('/').Last() : "").FirstOrDefault(id => Regex.IsMatch(id, @"^Q\d+$")) ?? "" : "";
        if (wikidataId != "") try
        {
            var sourced = "FILTER EXISTS { ?statement prov:wasDerivedFrom ?reference . ?reference ?referenceProperty ?referenceValue . FILTER(REGEX(STR(?referenceProperty), \"^http://www.wikidata.org/prop/reference/P[0-9]+$\") && ?referenceProperty NOT IN (pr:P143, pr:P4656, pr:P813, pr:P887, pr:P3452)) }"; // a real source, not "imported from Wikipedia" or a retrieval date
            var labelLanguages = L.Code == "en" ? "en" : L.Code + ",en"; // names in the app's language where Wikidata has them
            var sparql = $@"SELECT ?kind ?itemLabel ?date ?end WHERE {{
  {{ wd:{wikidataId} p:P463 ?statement . ?statement ps:P463 ?item ; pq:P580 ?date . OPTIONAL {{ ?statement pq:P582 ?end }} ?item wdt:P31/wdt:P279* wd:Q2088357 . BIND(""member"" AS ?kind) }}
  UNION {{ wd:{wikidataId} p:P264 ?statement . ?statement ps:P264 ?item ; pq:P580 ?date . OPTIONAL {{ ?statement pq:P582 ?end }} BIND(""label"" AS ?kind) }}
  UNION {{ wd:{wikidataId} p:P166 ?statement . ?statement ps:P166 ?item ; pq:P585 ?date . BIND(""award"" AS ?kind) }}
  UNION {{ wd:{wikidataId} p:P1411 ?statement . ?statement ps:P1411 ?item ; pq:P585 ?date . BIND(""nominated"" AS ?kind) }}
  {sourced}
  SERVICE wikibase:label {{ bd:serviceParam wikibase:language ""{labelLanguages}"". }}
}}";
            var sparqlAddress = "https://query.wikidata.org/sparql?format=json&query=" + Uri.EscapeDataString(sparql);
            if ((CachedAnswer(sparqlAddress, TimeSpan.FromDays(30)) is { } cachedSparql ? cachedSparql.Answer : await KeptAsync(sparqlAddress)) is { } answer && answer.TryGetProperty("results", out var results) && results.TryGetProperty("bindings", out var bindings))
            {
                string Value(JsonElement binding, string name) => binding.TryGetProperty(name, out var field) ? LyrionClient.ReadString(field, "value") : "";
                foreach (var binding in bindings.EnumerateArray())
                {
                    var label = Value(binding, "itemLabel");
                    if (label == "" || Regex.IsMatch(label, @"^Q\d+$")) continue; // no English name
                    var kind = Value(binding, "kind");
                    if (events.Any(existing => (string)existing["kind"] == kind && (string)existing["title"] == label && (int)existing["year"] == YearOf(Value(binding, "date")))) continue;
                    Add(YearOf(Value(binding, "date")), kind, label, endYear: YearOf(Value(binding, "end")));
                }
            }
        }
        catch (Exception error) { CrashReporter.Note("Timeline Wikidata: " + error.Message); albumsIncomplete = true; }
        if (!events.Any(item => (string)item["kind"] != "start" && (string)item["kind"] != "end")) return new(false, Incomplete: albumsIncomplete);
        var kindOrder = new[] { "start", "member", "label", "album", "award", "nominated", "end" };
        var ordered = events.OrderBy(item => (int)item["year"]).ThenBy(item => Array.IndexOf(kindOrder, (string)item["kind"])).ThenBy(item => (string)item["title"]).ToList();
        return new(true, request.Artist, isPerson ? "person" : "group", Source: L.T("MusicBrainz and Wikidata (sourced facts only)"), Link: $"https://musicbrainz.org/artist/{LyrionClient.ReadString(artist, "id")}", Events: ordered, Incomplete: albumsIncomplete);
    }

    // ---- Credits: who played, sang, wrote, produced and engineered the album, track by track (SPEC.md › Now playing views) ----
    // MusicBrainz relationships on the release (production, artwork), its recordings (performers, production) and their works (writers)
    static readonly string[] roleModifiers = { "co", "executive", "assistant", "associate", "additional", "guest", "solo", "lead" };
    static readonly string[] performerTypes = { "instrument", "vocal", "performer", "performing orchestra", "conductor", "chorus master", "concertmaster" };
    static readonly string[] writerTypes = { "composer", "lyricist", "writer", "librettist", "arranger", "orchestrator", "instrument arranger", "vocal arranger", "translator", "revised by" };
    static readonly string[] artworkTypes = { "design/illustration", "design", "illustration", "photography", "art direction", "graphic design", "artwork", "creative direction", "liner notes" };
    static readonly string[] skippedCreditTypes = { "misc", "tribute", "dedicated to", "samples from", "phonographic copyright", "copyright", "legal representation", "booking", "publishing", "rights society", "licensor" };
    static readonly Dictionary<string, string> roleNames = new() { ["mix"] = "mixing", ["audio"] = "audio engineer", ["sound"] = "sound engineer", ["performing orchestra"] = "orchestra", ["design/illustration"] = "design and illustration", ["editor"] = "editing", ["instrument arranger"] = "arranger", ["recording"] = "recording engineer", ["vocal"] = "vocals", ["instrument"] = "instruments" };
    async Task<Result> CreditsAsync(Request request)
    {
        if (request.Artist == "" || request.Album == "" || await MusicBrainzEntityAsync("album", request.Artist, request.Album) is not { } releaseGroup) return new(false);
        if (await MusicBrainzAsync($"https://musicbrainz.org/ws/2/release?release-group={LyrionClient.ReadString(releaseGroup, "id")}&status=official&limit=100&fmt=json") is not { } browse || !browse.TryGetProperty("releases", out var releases)) return new(false);
        // Credits usually sit on one or two of an album's many pressings: try the three earliest and keep the best-credited
        var candidates = releases.EnumerateArray().OrderBy(release => LyrionClient.ReadString(release, "date") is { Length: > 0 } date ? date : "9999").Select(release => LyrionClient.ReadString(release, "id")).Take(3).ToList();
        (List<Dictionary<string, object>> Tracks, List<Dictionary<string, object>> Credits, string Id, string Subheading)? best = null; Exception? failure = null;
        foreach (var releaseId in candidates)
        {
            try
            {
                if (await MusicBrainzAsync($"https://musicbrainz.org/ws/2/release/{releaseId}?inc=artist-rels+recordings+recording-level-rels+work-rels+work-level-rels&fmt=json") is not { } release) continue;
                var read = ReadCredits(release);
                if (best == null || read.Credits.Count > best.Value.Credits.Count) best = (read.Tracks, read.Credits, releaseId, string.Join(" · ", new[] { LyrionClient.ReadString(release, "date"), LyrionClient.ReadString(release, "country") }.Where(part => part != "")));
                if (read.Credits.Count >= 15) break;
            }
            catch (Exception error) { CrashReporter.Note("Credits: " + error.Message); failure = error; }
        }
        if (best is not { } chosen || chosen.Credits.Count == 0) { if (failure != null) throw failure; return new(false); } // a failed lookup is asked again next time, not remembered as "no credits"
        return new(true, request.Album, chosen.Subheading, Source: "MusicBrainz", Link: $"https://musicbrainz.org/release/{chosen.Id}", Events: new { tracks = chosen.Tracks, credits = chosen.Credits });
    }

    static (List<Dictionary<string, object>> Tracks, List<Dictionary<string, object>> Credits) ReadCredits(JsonElement release)
    {
        var tracks = new List<Dictionary<string, object>>(); var credits = new List<Dictionary<string, object>>();
        void Read(JsonElement owner, int trackNumber)
        {
            if (!owner.TryGetProperty("relations", out var relations)) return;
            foreach (var relation in relations.EnumerateArray())
            {
                var type = LyrionClient.ReadString(relation, "type");
                if (LyrionClient.ReadString(relation, "target-type") == "work" && relation.TryGetProperty("work", out var work)) { Read(work, trackNumber); continue; } // the song's writers
                if (LyrionClient.ReadString(relation, "target-type") != "artist" || !relation.TryGetProperty("artist", out var artist) || skippedCreditTypes.Contains(type)) continue;
                var name = LyrionClient.ReadString(relation, "target-credit") is { Length: > 0 } credited ? credited : LyrionClient.ReadString(artist, "name");
                var attributes = relation.TryGetProperty("attributes", out var list) ? list.EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item != "").ToList() : new();
                var group = performerTypes.Contains(type) ? "performer" : writerTypes.Contains(type) ? "writer" : artworkTypes.Contains(type) ? "artwork" : "production";
                var roles = new List<string>();
                if (type is "instrument" or "vocal") roles.AddRange(attributes.Where(item => !roleModifiers.Contains(item)).Select(item => type == "vocal" && !item.Contains("vocal") ? item + " vocals" : item));
                if (roles.Count == 0) roles.Add(string.Concat(attributes.Where(item => roleModifiers.Contains(item)).Select(item => item == "co" ? "co-" : item + " ")) + (roleNames.TryGetValue(type, out var named) ? named : type)); // "co-producer", "additional engineer"
                foreach (var role in roles)
                {
                    var existing = credits.FirstOrDefault(credit => (string)credit["name"] == name && (string)credit["role"] == role);
                    if (existing == null) credits.Add(existing = new() { ["name"] = name, ["role"] = role, ["group"] = group, ["tracks"] = new List<int>() });
                    if (trackNumber > 0 && existing["tracks"] is List<int> numbers && !numbers.Contains(trackNumber)) numbers.Add(trackNumber);
                }
            }
        }
        Read(release, 0); // the whole album
        if (release.TryGetProperty("media", out var media))
        {
            var discs = media.GetArrayLength(); var running = 0;
            foreach (var medium in media.EnumerateArray())
            {
                if (!medium.TryGetProperty("tracks", out var discTracks)) continue;
                foreach (var track in discTracks.EnumerateArray())
                {
                    running++;
                    var number = LyrionClient.ReadString(track, "number") is { Length: > 0 } printed ? printed : LyrionClient.ReadString(track, "position");
                    tracks.Add(new() { ["n"] = running, ["label"] = discs > 1 ? $"{LyrionClient.ReadString(medium, "position")}.{number}" : number, ["title"] = LyrionClient.ReadString(track, "title") });
                    if (track.TryGetProperty("recording", out var recording)) Read(recording, running);
                }
            }
        }
        foreach (var credit in credits) if (credit["tracks"] is List<int> numbers) { numbers.Sort(); credit["all"] = numbers.Count == tracks.Count; if (numbers.Count == tracks.Count) numbers.Clear(); } // on every track: shown without numbers, and on each track
        return (tracks, credits);
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

    // MusicBrainz answers 503 when busy and big lookups can time out: up to three tries, waiting longer each time. Every answer is kept on disk for 30 days,
    // so an artist or album is asked about once a month at most, not on every play or restart; when MusicBrainz can't answer, an older copy is used
    async Task<JsonElement?> MusicBrainzAsync(string address)
    {
        if (CachedAnswer(address, TimeSpan.FromDays(30)) is { } fresh) return fresh.Answer;
        for (var attempt = 1; ; attempt++)
        {
            await musicBrainzGate.WaitAsync();
            try
            {
                if (DateTime.UtcNow < musicBrainzBusyUntil) throw new HttpRequestException("MusicBrainz is busy", null, HttpStatusCode.ServiceUnavailable);
                var wait = lastMusicBrainzCall.AddMilliseconds(1100) - DateTime.UtcNow; if (wait > TimeSpan.Zero) await Task.Delay(wait); lastMusicBrainzCall = DateTime.UtcNow;
                var answer = await GetJsonAsync(address); KeepAnswer(address, answer); return answer;
            }
            catch (Exception error) when (error is TaskCanceledException || error is HttpRequestException { StatusCode: HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests or null })
            {
                if (attempt >= 3) { musicBrainzBusyUntil = DateTime.UtcNow.AddMinutes(2); if (CachedAnswer(address, TimeSpan.MaxValue) is { } stale) return stale.Answer; throw; } // still busy: leave it alone for two minutes
                lastMusicBrainzCall = DateTime.UtcNow.AddSeconds(attempt * 2);
            }
            finally { musicBrainzGate.Release(); }
        }
    }
    DateTime musicBrainzBusyUntil = DateTime.MinValue;

    // The disk cache: one file per address in <data>\InfoCache, its age from the file's time; "null" is a remembered "not found"
    static readonly string cacheFolder = Path.Combine(AppPaths.DataFolder, "InfoCache");
    static string CacheFile(string address) => Path.Combine(cacheFolder, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(address))) + ".json");
    static (JsonElement? Answer, bool Hit)? CachedAnswer(string address, TimeSpan maxAge)
    {
        try
        {
            var file = new FileInfo(CacheFile(address));
            if (!file.Exists || maxAge != TimeSpan.MaxValue && DateTime.UtcNow - file.LastWriteTimeUtc > maxAge) return null;
            var text = File.ReadAllText(file.FullName);
            if (text == "null") return (null, true);
            using var document = JsonDocument.Parse(text); return (document.RootElement.Clone(), true);
        }
        catch { return null; }
    }
    static void KeepAnswer(string address, JsonElement? answer) { try { Directory.CreateDirectory(cacheFolder); File.WriteAllText(CacheFile(address), answer is { } found ? found.GetRawText() : "null"); } catch { } }

    static async Task<JsonElement?> KeptAsync(string address) { var answer = await GetJsonAsync(address); KeepAnswer(address, answer); return answer; }

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
    static string CleanLyrics(string lyrics) => Regex.Replace(Regex.Replace(lyrics.Replace("\r\n", "\n").Replace('\r', '\n'), @"^\[[a-z]+:[^\]]*\]\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase), @"^(\[\d+:\d+(?:[.:]\d+)?\])+\s?", "", RegexOptions.Multiline).Replace("\n\n\n", "\n\n").Trim(); // timestamps and [ar:…] tags from synced (LRC) lyrics
    // Lyrics with their timed lines (when the text is synced LRC), so Now playing can follow the song line by line
    Result LyricsFound(Request request, string lyrics, string source, string link = "", string synced = "") => Found(request, CleanLyrics(lyrics), source, link: link) with { Events = TimedLines(synced != "" ? synced : lyrics) };
    static List<object>? TimedLines(string lyrics)
    {
        var offsetMatch = Regex.Match(lyrics, @"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase);
        var offsetSeconds = offsetMatch.Success ? int.Parse(offsetMatch.Groups[1].Value) / 1000.0 : 0; // LRC offset: positive shows lines sooner
        var timed = new List<(double Seconds, string Text)>();
        foreach (var line in lyrics.Replace("\r\n", "\n").Split('\n'))
        {
            var stamps = Regex.Match(line, @"^\s*((?:\[\d+:\d+(?:[.:]\d+)?\])+)(.*)$");
            if (!stamps.Success) continue;
            var text = Regex.Replace(stamps.Groups[2].Value, @"<\d+:\d+(?:[.:]\d+)?>", "").Trim(); // enhanced LRC's per-word stamps
            foreach (Match stamp in Regex.Matches(stamps.Groups[1].Value, @"\[(\d+):(\d+)(?:[.:](\d+))?\]"))
            {
                var fraction = stamp.Groups[3].Success ? double.Parse("0." + stamp.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
                timed.Add((Math.Max(0, int.Parse(stamp.Groups[1].Value) * 60 + int.Parse(stamp.Groups[2].Value) + fraction - offsetSeconds), text));
            }
        }
        return timed.Count(line => line.Text != "") < 2 ? null : timed.OrderBy(line => line.Seconds).Select(line => (object)new { t = Math.Round(line.Seconds, 2), text = line.Text }).ToList();
    }
}
