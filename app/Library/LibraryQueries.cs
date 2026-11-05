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
    readonly bool anyCredit;
    const string albumColumns = "a.key, a.title, a.artist, a.year, a.cover_id AS cover, a.track_count AS tracks, a.duration, a.format, a.added";

    public LibraryQueries(ServerProfile profile, LibrarySettings librarySettings)
    {
        databasePath = DatabasePath(profile);
        albumSource = librarySettings.AlbumSource == "tags" ? "t" : "f";
        anyCredit = librarySettings.ContributorGrouping != "albumartist";
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
            "artists" => Query(connection, $"SELECT part_norm AS key, display AS name, {CountColumn} AS albums FROM people WHERE role='artist' AND {CountColumn}>0 ORDER BY part_norm"),
            "composers" => Query(connection, $"SELECT part_norm AS key, display AS name, {CountColumn} AS albums FROM people WHERE role='composer' AND {CountColumn}>0 ORDER BY part_norm"),
            "performers" => Query(connection, $"SELECT part_norm AS key, display AS name, role, {CountColumn} AS albums FROM people WHERE role IN ('conductor','band','soloist') AND {CountColumn}>0 AND ($role='' OR role=$role) ORDER BY part_norm", ("$role", Text("role"))),
            "genres" => Query(connection, "SELECT g.id AS key, g.name, COUNT(DISTINCT tg.track_id) AS tracks FROM genres g JOIN track_genres tg ON tg.genre_id=g.id GROUP BY g.id ORDER BY g.name_norm"),
            "artistAlbums" => ArtistAlbums(connection, Text("key"), Text("sort")),
            "composerAlbums" => new { own = AlbumsByCredit(connection, Text("key"), new[] { "composer" }, Text("sort")) },
            "performerAlbums" => new { own = AlbumsByCredit(connection, Text("key"), new[] { "conductor", "band", "track" }, Text("sort")) },
            "genreAlbums" => new { own = Query(connection, $"SELECT DISTINCT {albumColumns} FROM albums a JOIN album_tracks at ON at.album_key=a.key JOIN track_genres tg ON tg.track_id=at.track_id WHERE a.source=$source AND tg.genre_id=$genre ORDER BY {AlbumOrder(Text("sort"))}", ("$source", albumSource), ("$genre", Text("key"))) },
            "albums" => new { own = Query(connection, $"SELECT {albumColumns} FROM albums a WHERE a.source=$source ORDER BY {AlbumOrder(Text("sort"))}", ("$source", albumSource)) },
            "folder" => Folder(connection, Text("key")),
            "album" => Album(connection, Text("key")),
            "albumTrackIds" => Query(connection, "SELECT track_id AS id FROM album_tracks WHERE album_key=$key ORDER BY disc_no, track_no", ("$key", Text("key"))).Select(row => row["id"]).ToList(),
            "trackUrls" => Query(connection, "SELECT id, url FROM tracks WHERE id IN (SELECT value FROM json_each($ids))", ("$ids", Text("ids"))),
            "search" => Search(connection, Text("text")),
            _ => new { error = $"Unknown query {queryName}" }
        });
    }

    object ArtistAlbums(SqliteConnection connection, string partNorm, string sort)
    {
        var own = AlbumsByCredit(connection, partNorm, new[] { "own" }, sort);
        var appearsCredits = anyCredit ? new[] { "with", "track", "composer", "conductor", "band" } : new[] { "with" };
        var ownKeys = own.Select(album => (string)album["key"]!).ToHashSet();
        var appearsOn = AlbumsByCredit(connection, partNorm, appearsCredits, sort).Where(album => !ownKeys.Contains((string)album["key"]!)).ToList();
        var name = Query(connection, "SELECT display FROM people WHERE part_norm=$part LIMIT 1", ("$part", partNorm)).FirstOrDefault()?["display"];
        return new { name, own, appearsOn };
    }

    List<Dictionary<string, object?>> AlbumsByCredit(SqliteConnection connection, string partNorm, string[] credits, string sort) =>
        Query(connection, $"SELECT DISTINCT {albumColumns} FROM albums a JOIN album_credits c ON c.album_key=a.key WHERE a.source=$source AND c.part_norm=$part AND c.credit IN (SELECT value FROM json_each($credits)) ORDER BY {AlbumOrder(sort)}",
            ("$source", albumSource), ("$part", partNorm), ("$credits", JsonSerializer.Serialize(credits)));

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

    // Prefix match on every word ("jarr" finds Keith Jarrett); album and track hits grouped by the current album source, so in folder mode they group by folder
    object Search(SqliteConnection connection, string searchText)
    {
        var words = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray())).Where(word => word != "").ToList();
        if (words.Count == 0) return new { };
        var matchExpression = "text:(" + string.Join(" AND ", words.Select(word => $"\"{word}\"*")) + ")";
        List<Dictionary<string, object?>> Hits(string kind, int limit) => Query(connection, "SELECT ref AS key, label AS name, detail FROM search WHERE search MATCH $match AND kind=$kind AND (source='' OR source=$source) ORDER BY rank LIMIT $limit", ("$match", matchExpression), ("$kind", kind), ("$source", albumSource), ("$limit", limit));
        var trackHits = Query(connection, $"SELECT t.id, t.title, t.composer, t.track_artist AS artist, t.duration, {albumColumns} FROM search s JOIN tracks t ON t.id=s.ref JOIN album_tracks at ON at.track_id=t.id JOIN albums a ON a.key=at.album_key AND a.source=$source WHERE search MATCH $match AND s.kind='track' ORDER BY rank LIMIT 120", ("$match", matchExpression), ("$source", albumSource));
        var trackGroups = trackHits.GroupBy(hit => (string)hit["key"]!).Select(group => new { album = group.First(), tracks = group.Select(hit => new { id = hit["id"], title = hit["title"], composer = hit["composer"], artist = hit["artist"], duration = hit["duration"] }).ToList() }).ToList();
        var bestArtist = Hits("artist", 1).FirstOrDefault();
        return new { artists = Hits("artist", 12), composers = Hits("composer", 12), performers = Hits("performer", 12), albums = Hits("album", 40), trackGroups, bestArtistAlbums = bestArtist == null ? null : AlbumsByCredit(connection, (string)bestArtist["key"]!, new[] { "own" }, "") };
    }
}
