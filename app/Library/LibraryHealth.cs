using System.IO;
using System.Text.RegularExpressions;
using static WinSqueeze.Library.LibraryDatabase;
namespace WinSqueeze.Library;

// Library health (SPEC.md › Library health): tag and file problems found in the local index. Read-only: nothing on the server or in the files changes
public static partial class LibraryHealth
{
    public const int Version = 1; // bump when the checks change, so cached reports are rebuilt
    static readonly string[] classicalGenreWords = { "classical", "baroque", "opera", "choral", "chamber", "orchestral", "symphon", "concert", "renaissance", "medieval", "early music", "lieder", "oratorio", "sacred" };
    [GeneratedRegex(@"[^\p{L}\p{N}]+")] private static partial Regex NonWordRegex();

    sealed class UnitTrack { public long Id, TagAlbumId; public int Disc, TrackNo, Year; public string TitleNorm = "", Composer = "", AlbumArtistTag = "", ArtistTag = ""; }
    sealed class Unit { public string Key = "", Title = "", Artist = "", Cover = "", Folder = "", Format = ""; public List<UnitTrack> Tracks = new(); }

    // Each check walks folder albums (one folder = one CD) when the library has files, else tag albums
    static string UnitSource(Microsoft.Data.Sqlite.SqliteConnection connection) => Convert.ToInt64(Query(connection, "SELECT COUNT(*) AS albums FROM albums WHERE source='f'")[0]["albums"]) > 0 ? "f" : "t";

    public static List<string> CoverIds(string databasePath)
    {
        using var connection = Open(databasePath, true);
        return Query(connection, "SELECT DISTINCT cover_id FROM albums WHERE source=$source AND cover_id<>''", ("$source", UnitSource(connection))).Select(row => (string)row["cover_id"]!).ToList();
    }

    public static Dictionary<string, object?> Build(string databasePath, string cacheKey, ICollection<string> coversWithoutArt)
    {
        using var connection = Open(databasePath, true);
        var rootPath = Meta(connection, "root_path") ?? "";
        var unitSource = UnitSource(connection);
        string Relative(object? path) { var folderPath = path as string ?? ""; return rootPath != "" && folderPath.StartsWith(rootPath) ? folderPath[rootPath.Length..].TrimStart('/') : folderPath; }
        var units = Query(connection, "SELECT a.key, a.title, a.artist, a.cover_id, a.format, f.path FROM albums a LEFT JOIN folders f ON f.id=a.folder_id WHERE a.source=$source", ("$source", unitSource))
            .ToDictionary(row => (string)row["key"]!, row => new Unit { Key = (string)row["key"]!, Title = row["title"] as string ?? "", Artist = row["artist"] as string ?? "", Cover = row["cover_id"] as string ?? "", Format = row["format"] as string ?? "", Folder = Relative(row["path"]) });
        // Raw artist and album artist tags per track, names sorted so the order on the track doesn't matter
        var creditNames = new Dictionary<(long trackId, string role), List<string>>();
        foreach (var row in Query(connection, "SELECT tc.track_id, tc.role, c.name FROM track_contributors tc JOIN contributors c ON c.id=tc.contributor_id WHERE tc.role IN ('albumartist','artist')"))
        { var creditKey = ((long)row["track_id"]!, (string)row["role"]!); if (!creditNames.TryGetValue(creditKey, out var names)) creditNames[creditKey] = names = new(); names.Add(row["name"] as string ?? ""); }
        string Credit(long trackId, string role) => creditNames.TryGetValue((trackId, role), out var names) ? string.Join(", ", names.Where(name => name != "").OrderBy(name => name)) : "";
        foreach (var row in Query(connection, "SELECT at.album_key, at.disc_no, t.id, t.track_no, t.title_norm, t.year, t.composer, t.tag_album_id FROM album_tracks at JOIN albums a ON a.key=at.album_key AND a.source=$source JOIN tracks t ON t.id=at.track_id ORDER BY at.disc_no, t.track_no, t.url", ("$source", unitSource)))
        {
            var trackId = (long)row["id"]!;
            units[(string)row["album_key"]!].Tracks.Add(new UnitTrack { Id = trackId, Disc = Convert.ToInt32(row["disc_no"] ?? 0L), TrackNo = Convert.ToInt32(row["track_no"] ?? 0L), Year = Convert.ToInt32(row["year"] ?? 0L), TitleNorm = row["title_norm"] as string ?? "", Composer = (row["composer"] as string ?? "").Trim(), TagAlbumId = Convert.ToInt64(row["tag_album_id"] ?? 0L), AlbumArtistTag = Credit(trackId, "albumartist"), ArtistTag = Credit(trackId, "artist") });
        }
        var tagAlbums = Query(connection, "SELECT key, title, title_norm, artist, artist_norm FROM albums WHERE source='t'").ToDictionary(row => (string)row["key"]!);
        var genreTest = string.Join(" OR ", classicalGenreWords.Select(word => $"g.name_norm LIKE '%{word}%'"));
        var classicalKeys = Query(connection, $"SELECT album_key AS key FROM album_credits WHERE credit IN ('conductor','band') UNION SELECT at.album_key FROM album_tracks at JOIN track_genres tg ON tg.track_id=at.track_id JOIN genres g ON g.id=tg.genre_id WHERE {genreTest}").Select(row => (string)row["key"]!).ToHashSet();
        var unitByTrack = new Dictionary<long, string>();
        foreach (var unit in units.Values) foreach (var track in unit.Tracks) unitByTrack.TryAdd(track.Id, unit.Key);
        Dictionary<string, object?> Row(Unit unit) => new() { ["key"] = unit.Key, ["title"] = unit.Title, ["artist"] = unit.Artist, ["cover"] = unit.Cover, ["folder"] = unit.Folder, ["tracks"] = unit.Tracks.Count, ["format"] = unit.Format };
        var ordered = units.Values.Where(unit => unit.Tracks.Count > 0).OrderBy(unit => unit.Artist, StringComparer.OrdinalIgnoreCase).ThenBy(unit => unit.Title, StringComparer.OrdinalIgnoreCase).ToList();

        // Missing art: no cover in the files, or the server only has its placeholder for it
        var missingArt = ordered.Where(unit => unit.Cover == "" || coversWithoutArt.Contains(unit.Cover)).Select(Row).ToList();

        // Missing composer, per album; classical albums (conductor, orchestra or a classical genre) are flagged so the view can show only those
        var missingComposer = new List<Dictionary<string, object?>>();
        foreach (var unit in ordered)
        {
            var missing = unit.Tracks.Count(track => track.Composer == "");
            if (missing == 0) continue;
            var row = Row(unit); row["missing"] = missing; row["classical"] = classicalKeys.Contains(unit.Key) || unit.Tracks.Any(track => track.TagAlbumId > 0 && classicalKeys.Contains("t:" + track.TagAlbumId));
            row["detail"] = missing == unit.Tracks.Count ? "No composer on any track" : $"No composer on {missing} of {unit.Tracks.Count} tracks";
            missingComposer.Add(row);
        }

        // Duplicates: identical track lists, or the same artist and title with at least half the tracks in common
        var parentOf = units.Keys.ToDictionary(key => key, key => key);
        string Find(string key) { while (parentOf[key] != key) key = parentOf[key] = parentOf[parentOf[key]]; return key; }
        void Join(string first, string second) => parentOf[Find(first)] = Find(second);
        string Fingerprint(Unit unit) => string.Join("\u0001", unit.Tracks.Select(track => track.TitleNorm).OrderBy(title => title, StringComparer.Ordinal));
        foreach (var group in ordered.Where(unit => unit.Tracks.Count >= 2).GroupBy(Fingerprint).Where(group => group.Count() > 1)) foreach (var unit in group.Skip(1)) Join(group.First().Key, unit.Key);
        string Identity(Unit unit)
        {
            var tagAlbumId = unit.Tracks.GroupBy(track => track.TagAlbumId).OrderByDescending(group => group.Count()).First().Key;
            var identityRow = unitSource == "t" ? tagAlbums.GetValueOrDefault(unit.Key) : tagAlbums.GetValueOrDefault("t:" + tagAlbumId);
            var titleNorm = identityRow?["title_norm"] as string ?? "";
            return titleNorm is "" or "no album" or "unknown album" ? "" : titleNorm + "\u0001" + (identityRow?["artist_norm"] as string ?? "");
        }
        foreach (var group in ordered.GroupBy(Identity).Where(group => group.Key != "" && group.Count() > 1))
        {
            var members = group.ToList();
            for (var first = 0; first < members.Count; first++)
                for (var second = first + 1; second < members.Count; second++)
                {
                    var shared = members[first].Tracks.Select(track => track.TitleNorm).Intersect(members[second].Tracks.Select(track => track.TitleNorm)).Count();
                    if (shared > 0 && shared * 2 >= Math.Min(members[first].Tracks.Count, members[second].Tracks.Count)) Join(members[first].Key, members[second].Key);
                }
        }
        var duplicates = ordered.GroupBy(unit => Find(unit.Key)).Where(group => group.Count() > 1).Select(group =>
        {
            var members = group.ToList();
            return new Dictionary<string, object?> { ["reason"] = members.Select(Fingerprint).Distinct().Count() == 1 ? "Identical track lists" : "Same artist and title, tracks partly the same", ["albums"] = members.Select(Row).ToList() };
        }).ToList();

        // Bad or inconsistent tags, one row per album with each problem found
        var currentYear = DateTime.Now.Year;
        var tags = new List<Dictionary<string, object?>>();
        foreach (var unit in ordered)
        {
            var problems = new List<Dictionary<string, string>>();
            void Problem(string kind, string text) => problems.Add(new() { ["kind"] = kind, ["text"] = text });
            var tracks = unit.Tracks;
            var withAlbumArtist = tracks.Where(track => track.AlbumArtistTag != "").ToList();
            if (tracks.All(track => track.AlbumArtistTag == "" && track.ArtistTag == "")) Problem("artist", "No artist or album artist on any track");
            else if (withAlbumArtist.Count > 0 && withAlbumArtist.Count < tracks.Count) Problem("artist", $"Album artist missing on {tracks.Count - withAlbumArtist.Count} of {tracks.Count} tracks");
            else if (withAlbumArtist.Select(track => track.AlbumArtistTag).Distinct().Count() > 1) Problem("artist", $"Album artist differs between tracks: {Few(withAlbumArtist.Select(track => track.AlbumArtistTag).Distinct().ToList(), 3, " / ")}");
            else if (withAlbumArtist.Count == 0 && tracks.Select(track => track.ArtistTag).Where(artist => artist != "").Distinct().Count() > 1) Problem("artist", "No album artist, and the tracks have different artists");
            if (unitSource == "f")
            {
                var tagAlbumIds = tracks.Select(track => track.TagAlbumId).Where(tagAlbumId => tagAlbumId > 0).Distinct().ToList();
                if (tagAlbumIds.Count > 1) Problem("split", $"Tagged as {tagAlbumIds.Count} albums: {Few(tagAlbumIds.Select(tagAlbumId => tagAlbums.TryGetValue("t:" + tagAlbumId, out var tagAlbum) ? $"“{tagAlbum["title"]}”" + (tagAlbum["artist"] is string { Length: > 0 } tagArtist ? $" ({tagArtist})" : "") : "?").ToList(), 3, "; ")}");
            }
            var discs = tracks.GroupBy(track => track.Disc).OrderBy(group => group.Key).ToList();
            foreach (var disc in discs)
            {
                var prefix = discs.Count > 1 ? $"Disc {(disc.Key > 0 ? disc.Key : "?")}: " : "";
                var discTracks = disc.ToList();
                var unnumbered = discTracks.Count(track => track.TrackNo <= 0);
                if (unnumbered == discTracks.Count) { Problem("numbers", prefix + "No track numbers"); continue; }
                if (unnumbered > 0) Problem("numbers", prefix + $"No track number on {unnumbered} {(unnumbered == 1 ? "track" : "tracks")}");
                var repeated = discTracks.Where(track => track.TrackNo > 0).GroupBy(track => track.TrackNo).Where(group => group.Count() > 1).Select(group => group.Key).OrderBy(number => number).ToList();
                if (repeated.Count > 0) Problem("numbers", prefix + $"{(repeated.Count == 1 ? "Track" : "Tracks")} {Few(repeated.Select(number => number.ToString()).ToList(), 6, ", ")} {(repeated.Count == 1 ? "appears" : "appear")} more than once");
                var numbers = discTracks.Where(track => track.TrackNo > 0).Select(track => track.TrackNo).ToHashSet();
                var highest = numbers.Max();
                var gaps = highest <= 199 ? Enumerable.Range(1, highest).Where(number => !numbers.Contains(number)).ToList() : new List<int>();
                if (gaps.Count > 0) Problem("numbers", prefix + $"{(gaps.Count == 1 ? "Track" : "Tracks")} {Few(gaps.Select(number => number.ToString()).ToList(), 6, ", ")} missing (numbered up to {highest})");
            }
            var years = tracks.Select(track => track.Year).Where(year => year > 0).Distinct().OrderBy(year => year).ToList();
            if (years.Count == 0) Problem("year", "No year");
            foreach (var oddYear in years.Where(year => year < 1900 || year > currentYear + 1)) Problem("year", $"Odd year: {oddYear}");
            if (years.Count > 1) Problem("year", $"Tracks have different years: {Few(years.Select(year => year.ToString()).ToList(), 4, ", ")}");
            if (problems.Count == 0) continue;
            var row = Row(unit); row["problems"] = problems;
            tags.Add(row);
        }

        // Names split in two: two names (same role) that only ever appear together, where one is a single word or together they make a name the library already has ("Earth" + "Wind & Fire")
        var contributorNames = Query(connection, "SELECT id, name FROM contributors").ToDictionary(row => (long)row["id"]!, row => row["name"] as string ?? "");
        var knownNames = contributorNames.Values.Select(TextNormaliser.Normalise).ToHashSet();
        var tracksOf = new Dictionary<(long contributorId, string role), int>();
        var creditsByTrack = new Dictionary<(long trackId, string role), List<long>>();
        foreach (var row in Query(connection, "SELECT track_id, contributor_id, role FROM track_contributors WHERE role IN ('artist','albumartist','trackartist','composer','conductor','band') ORDER BY rowid"))
        {
            var (trackId, contributorId, role) = ((long)row["track_id"]!, (long)row["contributor_id"]!, (string)row["role"]!);
            tracksOf[(contributorId, role)] = tracksOf.GetValueOrDefault((contributorId, role)) + 1;
            if (!creditsByTrack.TryGetValue((trackId, role), out var credited)) creditsByTrack[(trackId, role)] = credited = new();
            credited.Add(contributorId);
        }
        var pairs = new Dictionary<(long first, long second, string role), (int together, long trackId)>();
        foreach (var ((trackId, role), credited) in creditsByTrack)
            for (var first = 0; first < credited.Count && credited.Count <= 12; first++)
                for (var second = first + 1; second < credited.Count; second++)
                { var pairKey = (credited[first], credited[second], role); var known = pairs.GetValueOrDefault(pairKey); pairs[pairKey] = (known.together + 1, known.trackId == 0 ? trackId : known.trackId); }
        bool SingleWord(string name) => name.Length >= 2 && name.All(char.IsLetter);
        var splitNames = new Dictionary<(long, long), Dictionary<string, object?>>();
        foreach (var ((firstId, secondId, role), (together, trackId)) in pairs)
        {
            if (together != tracksOf[(firstId, role)] || together != tracksOf[(secondId, role)] || splitNames.ContainsKey((firstId, secondId))) continue;
            var (firstName, secondName) = (contributorNames.GetValueOrDefault(firstId, ""), contributorNames.GetValueOrDefault(secondId, ""));
            var joined = knownNames.Contains(TextNormaliser.Normalise(firstName + ", " + secondName)) || knownNames.Contains(TextNormaliser.Normalise(firstName + " & " + secondName));
            if (!joined && !SingleWord(firstName) && !SingleWord(secondName)) continue;
            splitNames[(firstId, secondId)] = new() { ["names"] = new[] { firstName, secondName }, ["role"] = role, ["tracks"] = together, ["key"] = unitByTrack.GetValueOrDefault(trackId, ""), ["title"] = units.TryGetValue(unitByTrack.GetValueOrDefault(trackId, ""), out var unitOfTrack) ? unitOfTrack.Title : "", ["sure"] = joined };
        }

        // One person spelled two ways: the same words in another order or with other punctuation ("Bach, Johann Sebastian" and "Johann Sebastian Bach")
        var spelledTwice = Query(connection, "SELECT part_norm, role, display, album_count, album_count_t FROM people WHERE role IN ('artist','composer','conductor')")
            .GroupBy(row => (role: (string)row["role"]!, words: string.Join(" ", NonWordRegex().Split((string)row["part_norm"]!).Where(word => word != "").OrderBy(word => word, StringComparer.Ordinal))))
            .Where(group => group.Key.words.Contains(' ') && group.Count() > 1)
            .Select(group => new Dictionary<string, object?> { ["role"] = group.Key.role, ["people"] = group.Select(row => new { key = row["part_norm"], name = row["display"], albums = unitSource == "f" ? row["album_count"] : row["album_count_t"] }).ToList() })
            .OrderBy(group => group["role"]).ToList();

        return new()
        {
            ["cacheKey"] = cacheKey, ["version"] = Version, ["builtAt"] = DateTime.UtcNow.ToString("O"), ["folders"] = unitSource == "f", ["albums"] = ordered.Count, ["tracks"] = ordered.Sum(unit => unit.Tracks.Count),
            ["missingArt"] = missingArt, ["missingComposer"] = missingComposer, ["duplicates"] = duplicates, ["tags"] = tags,
            ["splitNames"] = splitNames.Values.OrderBy(row => ((string[])row["names"]!)[0], StringComparer.OrdinalIgnoreCase).ToList(), ["spelledTwice"] = spelledTwice
        };
    }

    static string Few(List<string> items, int limit, string separator) => string.Join(separator, items.Take(limit)) + (items.Count > limit ? $" and {items.Count - limit} more" : "");
}
