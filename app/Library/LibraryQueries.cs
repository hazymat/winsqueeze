using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WinSqueeze.Settings;
using static WinSqueeze.Library.LibraryDatabase;
namespace WinSqueeze.Library;

// All browsing and search, served from the local index (SPEC.md › Browsing)
public sealed class LibraryQueries
{
    readonly string databasePath;
    readonly string albumSource; // "f" folders or "t" tags
    readonly bool anyCredit, hideOwnComposers;
    readonly string nameSeparators;
    readonly bool readSurnameFirst;
    const string albumColumns = "a.key, a.title, a.artist, a.year, a.cover_id AS cover, a.track_count AS tracks, a.duration, a.format, a.added";

    public LibraryQueries(ServerProfile profile, LibrarySettings librarySettings)
    {
        databasePath = DatabasePath(profile);
        albumSource = librarySettings.AlbumSource == "tags" ? "t" : "f";
        anyCredit = librarySettings.ContributorGrouping != "albumartist";
        hideOwnComposers = librarySettings.HideOwnComposers;
        nameSeparators = librarySettings.NameSeparators; readSurnameFirst = librarySettings.ReadSurnameFirst;
    }

    public bool HasIndex => File.Exists(databasePath);

    T WithConnection<T>(Func<SqliteConnection, T> work) { using var connection = Open(databasePath, true); return work(connection); }

    string AlbumOrder(string sort) => sort switch { "title" => "a.title_norm", "year" => "a.year, a.artist_norm", "added" => "a.added DESC", _ => "a.artist_norm, a.year, a.title_norm" };
    string CountColumn => albumSource == "f" ? "album_count" : "album_count_t";

    public object Run(string queryName, JsonElement queryParameters)
    {
        if (!HasIndex) return new { empty = true };
        string Text(string name) => queryParameters.ValueKind == JsonValueKind.Object && queryParameters.TryGetProperty(name, out var value) ? value.ToString() : "";
        return WithConnection<object>(connection => queryName switch
        {
            "stats" => new { tracks = Meta(connection, "track_count"), syncedAt = Meta(connection, "synced_at"), albums = Query(connection, "SELECT COUNT(*) AS albums FROM albums WHERE source=$source", ("$source", albumSource))[0]["albums"] },
            "artists" => WithCounts(connection, Query(connection, $"SELECT part_norm AS key, display AS name, {CountColumn} AS albums FROM people WHERE role='artist' AND {CountColumn}>0 ORDER BY sort_norm"), "SELECT part_norm AS key, album_key FROM album_credits WHERE credit IN ('own')"),
            // Pop tags credit singer-songwriters as composers; optionally keep only people who compose on albums that aren't their own
            "composers" => WithCounts(connection, Query(connection, $"SELECT part_norm AS key, display AS name, {CountColumn} AS albums FROM people p WHERE role='composer' AND {CountColumn}>0" + (hideOwnComposers ? " AND EXISTS (SELECT 1 FROM album_credits c JOIN albums a ON a.key=c.album_key AND a.source=$source WHERE c.part_norm=p.part_norm AND c.credit='composer' AND NOT EXISTS (SELECT 1 FROM album_credits o WHERE o.album_key=c.album_key AND o.part_norm=c.part_norm AND o.credit IN ('own','with')))" : "") + " ORDER BY sort_norm", ("$source", albumSource)), "SELECT part_norm AS key, album_key FROM album_credits WHERE credit='composer'"),
            "performers" => WithCounts(connection, Query(connection, $"SELECT part_norm AS key, display AS name, role, {CountColumn} AS albums FROM people WHERE role IN ('conductor','band','soloist') AND {CountColumn}>0 AND ($role='' OR role=$role) ORDER BY sort_norm", ("$role", Text("role"))), "SELECT part_norm AS key, album_key FROM album_credits WHERE credit IN ('conductor','band','track')"),
            "genres" => WithCounts(connection, Query(connection, "SELECT g.id AS key, g.name, COUNT(DISTINCT tg.track_id) AS genreTracks FROM genres g JOIN track_genres tg ON tg.genre_id=g.id GROUP BY g.id ORDER BY g.name_norm"), "SELECT DISTINCT tg.genre_id AS key, at.album_key FROM track_genres tg JOIN album_tracks at ON at.track_id=tg.track_id"),
            "artistAlbums" => ArtistAlbums(connection, Text("key"), Text("sort")),
            "composerAlbums" => new { own = AlbumsByCredit(connection, Text("key"), new[] { "composer" }, Text("sort")) },
            "performerAlbums" => new { own = AlbumsByCredit(connection, Text("key"), new[] { "conductor", "band", "track" }, Text("sort")) },
            "genreAlbums" => new { own = Query(connection, $"SELECT DISTINCT {albumColumns} FROM albums a JOIN album_tracks at ON at.album_key=a.key JOIN track_genres tg ON tg.track_id=at.track_id WHERE a.source=$source AND tg.genre_id=$genre ORDER BY {AlbumOrder(Text("sort"))}", ("$source", albumSource), ("$genre", Text("key"))) },
            "albums" => new { own = Query(connection, $"SELECT {albumColumns} FROM albums a WHERE a.source=$source ORDER BY {AlbumOrder(Text("sort"))}", ("$source", albumSource)) },
            "newAlbums" => new { own = NewAlbums(connection, 0, 100) }, // New music: the newest 100, as Lyrion's own New Music
            "folder" => Folder(connection, Text("key")),
            "album" => Album(connection, Text("key")),
            "albumTrackIds" => Query(connection, "SELECT track_id AS id FROM album_tracks WHERE album_key=$key ORDER BY disc_no, track_no", ("$key", Text("key"))).Select(row => row["id"]).ToList(),
            "trackUrls" => Query(connection, "SELECT id, url FROM tracks WHERE id IN (SELECT value FROM json_each($ids))", ("$ids", Text("ids"))),
            "search" => Search(connection, Text("text")),
            "trackPlace" => Query(connection, "SELECT a.key AS album, a.title AS albumTitle, (SELECT ac.part_norm FROM album_credits ac WHERE ac.album_key=a.key AND ac.credit='own' LIMIT 1) AS artist, (SELECT p.display FROM people p WHERE p.part_norm=(SELECT ac.part_norm FROM album_credits ac WHERE ac.album_key=a.key AND ac.credit='own' LIMIT 1) AND p.role='artist') AS artistName FROM album_tracks at JOIN albums a ON a.key=at.album_key AND a.source=$source WHERE at.track_id=$id LIMIT 1", ("$source", albumSource), ("$id", Text("id"))).FirstOrDefault(),
            "trackInfo" => Query(connection, "SELECT t.id, t.title, t.track_no, t.disc_no, t.duration, t.year, t.content_type, t.sample_rate, t.sample_size, t.bitrate, t.album_artist, t.track_artist, t.composer, t.url, (SELECT a.title FROM album_tracks at JOIN albums a ON a.key=at.album_key AND a.source=$source WHERE at.track_id=t.id LIMIT 1) AS album, (SELECT group_concat(g.name, ', ') FROM track_genres tg JOIN genres g ON g.id=tg.genre_id WHERE tg.track_id=t.id) AS genre FROM tracks t WHERE t.id IN (SELECT value FROM json_each($ids))", ("$source", albumSource), ("$ids", Text("ids"))),
            "customLevel" => CustomLevel(connection, queryParameters, Text("level")),
            "customAlbums" => new { own = Query(connection, $"WITH matched AS ({CustomTracks(queryParameters, out var albumParameters)}) SELECT DISTINCT {albumColumns} FROM albums a JOIN album_tracks at ON at.album_key=a.key JOIN matched m ON m.id=at.track_id WHERE a.source=$source ORDER BY {AlbumOrder(Text("sort"))}", albumParameters.Append(("$source", (object?)albumSource)).ToArray()) },
            // Raw tag text ("Kapustin, Nikolai; Bach, J.S.") to the first person's key, as the lists use it
            "personKey" => TextNormaliser.SplitNames(Text("name"), nameSeparators, Text("kind") is "composer" or "conductor", readSurnameFirst).Select(part => new { key = TextNormaliser.Normalise(part), name = Query(connection, "SELECT display FROM people WHERE part_norm=$part LIMIT 1", ("$part", TextNormaliser.Normalise(part))).FirstOrDefault()?["display"] ?? part }).FirstOrDefault(),
            "trackItems" => TrackItems(connection, queryParameters.ValueKind == JsonValueKind.Object && queryParameters.TryGetProperty("items", out var itemList) && itemList.ValueKind == JsonValueKind.Array ? itemList : default),
            _ => new { error = $"Unknown query {queryName}" }
        });
    }

    // Keep playing: what to add when the queue runs out. Looks at the same album artist, then the playing track's genres, then anything, skipping what's queued or was added lately
    public ContinuePickResult ContinuePick(long trackId, bool wholeAlbum, string lookFrom, List<long> queuedIds, List<string> recentKeys) => !HasIndex ? new(new(), new(), "") : WithConnection<ContinuePickResult>(connection =>
    {
        var current = Query(connection, "SELECT a.key, a.artist_norm FROM album_tracks at JOIN albums a ON a.key=at.album_key AND a.source=$source WHERE at.track_id=$track LIMIT 1", ("$source", albumSource), ("$track", trackId)).FirstOrDefault();
        var levels = new List<string>();
        if (lookFrom == "artist" && current != null) levels.Add("a.artist_norm=$artist");
        if (lookFrom != "anything") levels.Add("a.key IN (SELECT gt.album_key FROM album_tracks gt JOIN track_genres tg ON tg.track_id=gt.track_id WHERE tg.genre_id IN (SELECT genre_id FROM track_genres WHERE track_id=$track))");
        levels.Add("1");
        var picked = new List<long>();
        (string, object?)[] Parameters() => new (string, object?)[] { ("$source", albumSource), ("$track", trackId), ("$artist", current?["artist_norm"] ?? ""), ("$queued", JsonSerializer.Serialize(queuedIds)), ("$recent", JsonSerializer.Serialize(recentKeys)), ("$picked", JsonSerializer.Serialize(picked)) };
        const string notQueuedAlbum = "a.source=$source AND a.key NOT IN (SELECT album_key FROM album_tracks WHERE track_id IN (SELECT value FROM json_each($queued))) AND a.key NOT IN (SELECT value FROM json_each($recent))";
        foreach (var level in levels)
        {
            if (wholeAlbum)
            {
                var album = Query(connection, $"SELECT a.key, a.title, a.artist FROM albums a WHERE {notQueuedAlbum} AND {level} ORDER BY random() LIMIT 1", Parameters()).FirstOrDefault();
                if (album == null) continue;
                var albumIds = Query(connection, "SELECT track_id AS id FROM album_tracks WHERE album_key=$key ORDER BY disc_no, track_no", ("$key", album["key"])).Select(row => Convert.ToInt64(row["id"])).ToList();
                return new(albumIds, new() { Convert.ToString(album["key"])! }, $"{album["title"]} by {album["artist"]}");
            }
            picked.AddRange(Query(connection, $"SELECT DISTINCT at.track_id AS id FROM album_tracks at JOIN albums a ON a.key=at.album_key WHERE a.source=$source AND {level} AND at.track_id NOT IN (SELECT value FROM json_each($queued)) AND at.track_id NOT IN (SELECT value FROM json_each($picked)) AND ('t' || at.track_id) NOT IN (SELECT value FROM json_each($recent)) ORDER BY random() LIMIT {10 - picked.Count}", Parameters()).Select(row => Convert.ToInt64(row["id"])));
            if (picked.Count >= 10) break;
        }
        return new(picked, picked.Select(id => $"t{id}").ToList(), picked.Count == 1 ? "1 track" : $"{picked.Count} tracks");
    });

    object ArtistAlbums(SqliteConnection connection, string partNorm, string sort)
    {
        var own = AlbumsByCredit(connection, partNorm, new[] { "own" }, sort);
        var appearsCredits = anyCredit ? new[] { "with", "track", "composer", "conductor", "band" } : new[] { "with" };
        var ownKeys = own.Select(album => (string)album["key"]!).ToHashSet();
        var appearsOn = AlbumsByCredit(connection, partNorm, appearsCredits, sort).Where(album => !ownKeys.Contains((string)album["key"]!)).ToList();
        var name = Query(connection, "SELECT display FROM people WHERE part_norm=$part LIMIT 1", ("$part", partNorm)).FirstOrDefault()?["display"];
        return new { name, own, appearsOn };
    }

    // Top-level lists: per row, how many of each second-level grouping its albums make (album artists, years, decades, formats) plus albums and tracks, so the list can show the count that matches the view's grouping
    List<Dictionary<string, object?>> WithCounts(SqliteConnection connection, List<Dictionary<string, object?>> rows, string rowAlbumsSql)
    {
        var counts = Query(connection, $"SELECT ra.key, COUNT(DISTINCT a.key) AS albums, COUNT(DISTINCT a.artist_norm) AS artists, COUNT(DISTINCT NULLIF(a.year, 0)) AS years, COUNT(DISTINCT NULLIF(a.year, 0) / 10) AS decades, COUNT(DISTINCT a.format) AS formats, SUM(a.track_count) AS tracks FROM (SELECT DISTINCT key, album_key FROM ({rowAlbumsSql})) ra JOIN albums a ON a.key=ra.album_key AND a.source=$source GROUP BY ra.key", ("$source", albumSource))
            .ToDictionary(row => Convert.ToString(row["key"])!);
        AddCovers(connection, rows, "", rowAlbumsSql, ("$source", albumSource));
        foreach (var row in rows)
        {
            if (!counts.TryGetValue(Convert.ToString(row["key"])!, out var count)) continue;
            foreach (var name in new[] { "albums", "artists", "years", "decades", "formats", "tracks" }) row.TryAdd(name, count[name]); // keeps the list's own album count
            if (row.Remove("genreTracks", out var genreTracks)) row["tracks"] = genreTracks; // a genre's own tracks, not every track on its albums
        }
        return rows;
    }

    // Tiles: up to four covers per row (newest albums first), from SQL giving (key, album_key) rows
    void AddCovers(SqliteConnection connection, List<Dictionary<string, object?>> rows, string withPrefix, string keyAlbumsSql, params (string name, object? value)[] parameters)
    {
        var coversByKey = Query(connection, $"{withPrefix} SELECT key, group_concat(cover, '|') AS covers FROM (SELECT DISTINCT ka.key, a.cover_id AS cover, a.added FROM ({keyAlbumsSql}) ka JOIN albums a ON a.key=ka.album_key AND a.source=$source WHERE COALESCE(a.cover_id,'')<>'' ORDER BY a.added DESC) GROUP BY key", parameters)
            .ToDictionary(row => Convert.ToString(row["key"])!, row => Convert.ToString(row["covers"]) ?? "");
        foreach (var row in rows) row["covers"] = coversByKey.TryGetValue(Convert.ToString(row["key"])!, out var covers) ? covers.Split('|').Distinct().Take(4).ToList() : new List<string>();
    }

    List<Dictionary<string, object?>> AlbumsByCredit(SqliteConnection connection, string partNorm, string[] credits, string sort) =>
        Query(connection, $"SELECT DISTINCT {albumColumns} FROM albums a JOIN album_credits c ON c.album_key=a.key WHERE a.source=$source AND c.part_norm=$part AND c.credit IN (SELECT value FROM json_each($credits)) ORDER BY {AlbumOrder(sort)}",
            ("$source", albumSource), ("$part", partNorm), ("$credits", JsonSerializer.Serialize(credits)));

    // ---- Custom views (SPEC.md › Custom views) ----
    // Tracks in the view's genres (none chosen = all) that match every step taken so far ({kind, key} filters), as SQL selecting t.id
    static readonly Dictionary<string, string> creditsFor = new() { ["artist"] = "'own','with','track'", ["performer"] = "'conductor','band'" };
    string CustomTracks(JsonElement scope, out List<(string name, object? value)> parameters)
    {
        parameters = new();
        var conditions = new List<string>();
        var genreNames = scope.ValueKind == JsonValueKind.Object && scope.TryGetProperty("genres", out var genreList) && genreList.ValueKind == JsonValueKind.Array ? genreList.EnumerateArray().Select(genre => TextNormaliser.Normalise(genre.ToString())).ToList() : new();
        if (genreNames.Count > 0) { conditions.Add("t.id IN (SELECT tg.track_id FROM track_genres tg JOIN genres g ON g.id=tg.genre_id WHERE g.name_norm IN (SELECT value FROM json_each($scopeGenres)))"); parameters.Add(("$scopeGenres", JsonSerializer.Serialize(genreNames))); }
        var filterList = scope.ValueKind == JsonValueKind.Object && scope.TryGetProperty("filters", out var filters) && filters.ValueKind == JsonValueKind.Array ? filters.EnumerateArray().ToList() : new();
        for (var position = 0; position < filterList.Count; position++)
        {
            var filterKind = filterList[position].TryGetProperty("kind", out var kindValue) ? kindValue.GetString() ?? "" : "";
            var filterKey = filterList[position].TryGetProperty("key", out var keyValue) ? keyValue.ToString() : "";
            var parameterName = "$filter" + position;
            if (filterKind == "genre") conditions.Add($"t.id IN (SELECT track_id FROM track_genres WHERE genre_id={parameterName})");
            else if (filterKind == "composer") conditions.Add($"t.id IN (SELECT tc.track_id FROM contributor_parts cp JOIN track_contributors tc ON tc.contributor_id=cp.contributor_id AND tc.role='composer' WHERE cp.part_norm={parameterName})");
            else if (creditsFor.TryGetValue(filterKind, out var credits)) conditions.Add($"t.id IN (SELECT at.track_id FROM album_credits ac JOIN album_tracks at ON at.album_key=ac.album_key WHERE ac.part_norm={parameterName} AND ac.credit IN ({credits}))");
            else continue;
            parameters.Add((parameterName, filterKind == "genre" && long.TryParse(filterKey, out var genreId) ? genreId : filterKey));
        }
        return "SELECT t.id FROM tracks t" + (conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "");
    }

    // One step of a custom view: the genres, composers, artists or performers among its matching tracks, with album and track counts
    List<Dictionary<string, object?>> CustomLevel(SqliteConnection connection, JsonElement scope, string level)
    {
        var matched = CustomTracks(scope, out var parameters);
        parameters.Add(("$source", albumSource));
        var rowsBy = level switch
        {
            // Only the view's own genres, not other genres its tracks also carry
            "genre" => "SELECT DISTINCT CAST(g.id AS TEXT) AS key, m.id FROM matched m JOIN track_genres tg ON tg.track_id=m.id JOIN genres g ON g.id=tg.genre_id" + (parameters.Any(parameter => parameter.name == "$scopeGenres") ? " WHERE g.name_norm IN (SELECT value FROM json_each($scopeGenres))" : ""),
            "composer" => "SELECT DISTINCT cp.part_norm AS key, m.id FROM matched m JOIN track_contributors tc ON tc.track_id=m.id AND tc.role='composer' JOIN contributor_parts cp ON cp.contributor_id=tc.contributor_id",
            _ => $"SELECT DISTINCT ac.part_norm AS key, m.id FROM matched m JOIN album_tracks at ON at.track_id=m.id JOIN albums a ON a.key=at.album_key AND a.source=$source JOIN album_credits ac ON ac.album_key=a.key AND ac.credit IN ({creditsFor.GetValueOrDefault(level, creditsFor["artist"])})"
        };
        var names = level == "genre" ? "(SELECT name FROM genres WHERE id=CAST(rk.key AS INTEGER))" : $"COALESCE((SELECT display FROM people WHERE part_norm=rk.key ORDER BY role='{(level == "performer" ? "conductor" : level)}' DESC LIMIT 1), (SELECT part_display FROM contributor_parts WHERE part_norm=rk.key LIMIT 1), rk.key)";
        var sortName = level == "genre" ? "(SELECT name_norm FROM genres WHERE id=CAST(rk.key AS INTEGER))" : $"COALESCE((SELECT sort_norm FROM people WHERE part_norm=rk.key ORDER BY role='{(level == "performer" ? "conductor" : level)}' DESC LIMIT 1), rk.key)";
        var levelRows = Query(connection, $"WITH matched AS ({matched}), rk AS (SELECT rt.key, COUNT(DISTINCT rt.id) AS tracks, COUNT(DISTINCT a.key) AS albums FROM ({rowsBy}) rt LEFT JOIN album_tracks at ON at.track_id=rt.id LEFT JOIN albums a ON a.key=at.album_key AND a.source=$source GROUP BY rt.key) SELECT rk.key, {names} AS name, rk.tracks, rk.albums FROM rk ORDER BY {sortName}", parameters.ToArray());
        AddCovers(connection, levelRows, $"WITH matched AS ({matched})", $"SELECT DISTINCT rt.key, at.album_key FROM ({rowsBy}) rt JOIN album_tracks at ON at.track_id=rt.id", parameters.ToArray());
        return levelRows;
    }

    // Selection actions: any chosen rows (albums, people, genres, folders, tracks) as one ordered, de-duplicated track list
    const string trackColumns = "t.id, t.title, COALESCE(NULLIF(t.track_artist,''), t.album_artist, '') AS artist, t.duration";
    List<Dictionary<string, object?>> AlbumTracks(SqliteConnection connection, string albumKey, string extraCondition = "", params (string name, object? value)[] extraParameters) =>
        Query(connection, $"SELECT {trackColumns} FROM album_tracks at JOIN tracks t ON t.id=at.track_id WHERE at.album_key=$key {extraCondition} ORDER BY at.disc_no, at.track_no", new[] { ("$key", (object?)albumKey) }.Concat(extraParameters).ToArray());
    object TrackItems(SqliteConnection connection, JsonElement items)
    {
        var tracks = new List<Dictionary<string, object?>>();
        if (items.ValueKind != JsonValueKind.Array) return tracks;
        IEnumerable<string> AlbumKeys(List<Dictionary<string, object?>> albums) => albums.Select(album => (string)album["key"]!);
        foreach (var item in items.EnumerateArray())
        {
            var kind = item.TryGetProperty("kind", out var kindValue) ? kindValue.GetString() ?? "" : "";
            var key = item.TryGetProperty("key", out var keyValue) ? keyValue.ToString() : "";
            switch (kind)
            {
                case "album": tracks.AddRange(AlbumTracks(connection, key)); break;
                case "artist": var ownAlbums = AlbumsByCredit(connection, key, new[] { "own" }, ""); foreach (var albumKey in AlbumKeys(ownAlbums.Count > 0 ? ownAlbums : AlbumsByCredit(connection, key, new[] { "with", "track" }, ""))) tracks.AddRange(AlbumTracks(connection, albumKey)); break;
                case "composer": foreach (var albumKey in AlbumKeys(AlbumsByCredit(connection, key, new[] { "composer" }, ""))) tracks.AddRange(AlbumTracks(connection, albumKey, "AND EXISTS (SELECT 1 FROM track_contributors tc JOIN contributor_parts cp ON cp.contributor_id=tc.contributor_id WHERE tc.track_id=t.id AND tc.role='composer' AND cp.part_norm=$part)", ("$part", key))); break;
                case "performer": foreach (var albumKey in AlbumKeys(AlbumsByCredit(connection, key, new[] { "conductor", "band", "track" }, ""))) tracks.AddRange(AlbumTracks(connection, albumKey)); break;
                case "genre": tracks.AddRange(Query(connection, $"SELECT {trackColumns} FROM track_genres tg JOIN tracks t ON t.id=tg.track_id JOIN album_tracks at ON at.track_id=t.id JOIN albums a ON a.key=at.album_key AND a.source=$source WHERE tg.genre_id=$key ORDER BY a.artist_norm, a.year, a.title_norm, at.disc_no, at.track_no", ("$source", albumSource), ("$key", key))); break;
                case "folder": tracks.AddRange(Query(connection, $"SELECT {trackColumns} FROM tracks t JOIN folders f ON f.id=t.folder_id WHERE f.id=$id OR f.path LIKE (SELECT path FROM folders WHERE id=$id) || '/%' ORDER BY f.path, t.disc_no, t.track_no", ("$id", key))); break;
                // A custom view row: key is the view's scope as JSON ({genres, filters}); its matching tracks album by album
                case "custom": using (var scopeDocument = JsonDocument.Parse(key == "" ? "{}" : key)) { tracks.AddRange(Query(connection, $"WITH matched AS ({CustomTracks(scopeDocument.RootElement, out var scopeParameters)}) SELECT {trackColumns} FROM matched m JOIN tracks t ON t.id=m.id JOIN album_tracks at ON at.track_id=t.id JOIN albums a ON a.key=at.album_key AND a.source=$source ORDER BY a.artist_norm, a.year, a.title_norm, at.disc_no, at.track_no", scopeParameters.Append(("$source", (object?)albumSource)).ToArray())); } break;
                case "track": tracks.AddRange(Query(connection, $"SELECT {trackColumns} FROM tracks t WHERE t.id=$id", ("$id", key))); break;
            }
        }
        var seen = new HashSet<long>();
        return tracks.Where(track => seen.Add(Convert.ToInt64(track["id"]))).ToList();
    }

    // ---- Offline downloads (SPEC.md › Offline downloads) ----
    // Everything a download records about each track, with its album in the current album source
    public List<Dictionary<string, object?>> DownloadInfo(List<long> trackIds) => !HasIndex ? new() : WithConnection(connection => Query(connection, "SELECT t.id, t.title, COALESCE(NULLIF(t.track_artist,''), t.album_artist, '') AS artist, t.disc_no, t.track_no, t.duration, t.content_type, t.sample_rate, t.sample_size, t.year, t.url, a.key AS album_key, a.title AS album_title, a.artist AS album_artist, COALESCE(a.cover_id, t.cover_id) AS cover_id FROM tracks t LEFT JOIN albums a ON a.key=(SELECT at.album_key FROM album_tracks at JOIN albums sa ON sa.key=at.album_key AND sa.source=$source WHERE at.track_id=t.id LIMIT 1) WHERE t.id IN (SELECT value FROM json_each($ids))", ("$source", albumSource), ("$ids", JsonSerializer.Serialize(trackIds))));
    public List<long> TrackIds(JsonElement items) => !HasIndex ? new() : WithConnection(connection => ((List<Dictionary<string, object?>>)TrackItems(connection, items)).Select(track => Convert.ToInt64(track["id"])).ToList());
    public Dictionary<string, long> TrackIdsByUrl(List<string> urls) => !HasIndex ? new() : WithConnection(connection => Query(connection, "SELECT url, id FROM tracks WHERE url IN (SELECT value FROM json_each($urls))", ("$urls", JsonSerializer.Serialize(urls))).GroupBy(row => (string)row["url"]!).ToDictionary(group => group.Key, group => Convert.ToInt64(group.First()["id"])));
    // Albums with any downloaded track; downloaded = how many of its tracks are on this PC
    // Albums whose newest track the server first saw after a time (Lyrion's added time, which survives a clear and rescan)
    List<Dictionary<string, object?>> NewAlbums(SqliteConnection connection, long addedAfter, int limit) => Query(connection, $"SELECT {albumColumns} FROM albums a WHERE a.source=$source AND a.added>$after ORDER BY a.added DESC, a.artist_norm, a.title_norm LIMIT $limit", ("$source", albumSource), ("$after", addedAfter), ("$limit", limit));
    public List<Dictionary<string, object?>> AlbumsAddedAfter(long addedAfter) => !HasIndex ? new() : WithConnection(connection => NewAlbums(connection, addedAfter, 500));

    public object DownloadedAlbums(List<long> downloadedIds, string sort) => !HasIndex ? new { own = new List<object>() } : WithConnection<object>(connection => new { own = Query(connection, $"SELECT {albumColumns}, COUNT(DISTINCT at.track_id) AS downloaded FROM albums a JOIN album_tracks at ON at.album_key=a.key WHERE a.source=$source AND at.track_id IN (SELECT value FROM json_each($ids)) GROUP BY a.key ORDER BY {AlbumOrder(sort)}", ("$source", albumSource), ("$ids", JsonSerializer.Serialize(downloadedIds))) });
    public Dictionary<string, string> DownloadedAlbumKeys(List<long> downloadedIds) => !HasIndex || downloadedIds.Count == 0 ? new() : WithConnection(connection => Query(connection, "SELECT a.key, a.track_count, COUNT(DISTINCT at.track_id) AS downloaded FROM albums a JOIN album_tracks at ON at.album_key=a.key WHERE a.source=$source AND at.track_id IN (SELECT value FROM json_each($ids)) GROUP BY a.key", ("$source", albumSource), ("$ids", JsonSerializer.Serialize(downloadedIds))).ToDictionary(row => (string)row["key"]!, row => Convert.ToInt64(row["downloaded"]) >= Convert.ToInt64(row["track_count"]) ? "full" : "partial"));

    object Folder(SqliteConnection connection, string folderKey)
    {
        var folder = folderKey == "" ? Query(connection, "SELECT id, name, parent_id, album_key FROM folders WHERE parent_id IS NULL LIMIT 1").FirstOrDefault() : Query(connection, "SELECT id, name, parent_id, album_key FROM folders WHERE id=$id", ("$id", folderKey)).FirstOrDefault();
        if (folder == null) return new { children = new List<object>() };
        var children = Query(connection, "SELECT f.id AS key, f.name, f.track_count AS tracks, f.album_key AS albumKey, a.cover_id AS cover, a.artist, a.year, (SELECT COUNT(*) FROM folders c WHERE c.parent_id=f.id) AS subfolders FROM folders f LEFT JOIN albums a ON a.key=f.album_key WHERE f.parent_id=$id ORDER BY f.name_norm", ("$id", folder["id"]));
        return new { key = folder["id"], name = folder["name"], parent = folder["parent_id"], albumKey = folder["album_key"], children };
    }

    object Album(SqliteConnection connection, string albumKey)
    {
        var album = Query(connection, $"SELECT {albumColumns}, a.folder_id AS folder FROM albums a WHERE a.key=$key", ("$key", albumKey)).FirstOrDefault();
        var tracks = Query(connection, "SELECT t.id, t.title, at.disc_no AS disc, t.track_no AS number, t.duration, t.composer, t.track_artist AS artist, t.album_artist AS albumArtist, t.content_type AS type, t.sample_rate AS rate, t.sample_size AS bits, t.url FROM album_tracks at JOIN tracks t ON t.id=at.track_id WHERE at.album_key=$key ORDER BY at.disc_no, at.track_no, t.url", ("$key", albumKey));
        var folderPath = album?["folder"] is long folderId ? Query(connection, "SELECT path FROM folders WHERE id=$id", ("$id", folderId)).FirstOrDefault()?["path"] : null;
        return new { album, tracks, folderPath };
    }

    // Prefix match on every word ("jarr" finds Keith Jarrett); track columns are aliased so the album's title/artist/duration don't overwrite them; album and track hits grouped by the current album source, so in folder mode they group by folder
    object Search(SqliteConnection connection, string searchText)
    {
        var words = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray())).Where(word => word != "").ToList();
        if (words.Count == 0) return new { };
        var matchExpression = "text:(" + string.Join(" AND ", words.Select(word => $"\"{word}\"*")) + ")";
        List<Dictionary<string, object?>> Hits(string kind, int limit) => Query(connection, "SELECT ref AS key, label AS name, detail FROM search WHERE search MATCH $match AND kind=$kind AND (source='' OR source=$source) ORDER BY rank LIMIT $limit", ("$match", matchExpression), ("$kind", kind), ("$source", albumSource), ("$limit", limit));
        // Album and folder hits carry their cover and year, like the album lists
        List<Dictionary<string, object?>> AlbumHits() => Query(connection, "SELECT s.ref AS key, s.label AS name, s.detail, a.cover_id AS cover, a.year FROM search s LEFT JOIN albums a ON a.key=s.ref AND a.source=$source WHERE search MATCH $match AND s.kind='album' AND (s.source='' OR s.source=$source) ORDER BY rank LIMIT 40", ("$match", matchExpression), ("$source", albumSource));
        var trackHits = Query(connection, $"SELECT t.id, t.title AS trackTitle, t.composer, t.track_artist AS trackArtist, t.duration AS trackDuration, {albumColumns} FROM search s JOIN tracks t ON t.id=s.ref JOIN album_tracks at ON at.track_id=t.id JOIN albums a ON a.key=at.album_key AND a.source=$source WHERE search MATCH $match AND s.kind='track' ORDER BY rank LIMIT 120", ("$match", matchExpression), ("$source", albumSource));
        var trackGroups = trackHits.GroupBy(hit => (string)hit["key"]!).Select(group => new { album = group.First(), tracks = group.Select(hit => new { id = hit["id"], title = hit["trackTitle"], composer = hit["composer"], artist = hit["trackArtist"], duration = hit["trackDuration"] }).ToList() }).ToList();
        var bestArtist = Hits("artist", 1).FirstOrDefault();
        return new { artists = Hits("artist", 12), composers = Hits("composer", 12), performers = Hits("performer", 12), albums = AlbumHits(), trackGroups, bestArtistAlbums = bestArtist == null ? null : AlbumsByCredit(connection, (string)bestArtist["key"]!, new[] { "own" }, "") };
    }
}

public sealed record ContinuePickResult(List<long> Ids, List<string> Keys, string Label);
