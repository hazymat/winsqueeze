using System.IO;
using Microsoft.Data.Sqlite;
using WinSqueeze.Settings;
namespace WinSqueeze.Library;

// One SQLite index per server profile (SPEC.md › Library model and cache)
public static class LibraryDatabase
{
    public static string ProfileFolder(ServerProfile profile) => Directory.CreateDirectory(Path.Combine(AppPaths.DataFolder, "profiles", profile.Id)).FullName;
    public static string DatabasePath(ServerProfile profile) => Path.Combine(ProfileFolder(profile), "library.db");
    public static string ArtFolder(ServerProfile profile) => Directory.CreateDirectory(Path.Combine(ProfileFolder(profile), "art")).FullName;

    // Pooling off so the file can be swapped after a rebuild
    public static SqliteConnection Open(string databasePath, bool readOnly = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate }.ToString());
        connection.Open();
        if (!readOnly) Execute(connection, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;");
        return connection;
    }

    public const string Schema = @"
CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE contributors(id INTEGER PRIMARY KEY, name TEXT, name_norm TEXT);
CREATE TABLE contributor_parts(contributor_id INTEGER, part_norm TEXT, part_display TEXT, position INTEGER);
CREATE INDEX contributor_parts_norm ON contributor_parts(part_norm);
CREATE TABLE genres(id INTEGER PRIMARY KEY, name TEXT, name_norm TEXT);
CREATE TABLE folders(id INTEGER PRIMARY KEY, path TEXT UNIQUE, parent_id INTEGER, name TEXT, name_norm TEXT, track_count INTEGER DEFAULT 0, album_key TEXT);
CREATE INDEX folders_parent ON folders(parent_id);
CREATE TABLE tracks(id INTEGER PRIMARY KEY, title TEXT, title_norm TEXT, track_no INTEGER, disc_no INTEGER, duration REAL, url TEXT, folder_id INTEGER, tag_album_id INTEGER, year INTEGER, content_type TEXT, sample_rate INTEGER, sample_size INTEGER, bitrate TEXT, cover_id TEXT, added INTEGER, modified INTEGER, album_artist TEXT, track_artist TEXT, composer TEXT);
CREATE INDEX tracks_folder ON tracks(folder_id);
CREATE INDEX tracks_tag_album ON tracks(tag_album_id);
CREATE TABLE track_contributors(track_id INTEGER, contributor_id INTEGER, role TEXT);
CREATE INDEX track_contributors_track ON track_contributors(track_id);
CREATE INDEX track_contributors_contributor ON track_contributors(contributor_id, role);
CREATE TABLE track_genres(track_id INTEGER, genre_id INTEGER);
CREATE INDEX track_genres_genre ON track_genres(genre_id);
CREATE TABLE albums(key TEXT PRIMARY KEY, source TEXT, title TEXT, title_norm TEXT, artist TEXT, artist_norm TEXT, year INTEGER, cover_id TEXT, track_count INTEGER, duration REAL, added INTEGER, folder_id INTEGER, format TEXT);
CREATE INDEX albums_source ON albums(source, artist_norm, year);
CREATE TABLE album_tracks(album_key TEXT, track_id INTEGER, disc_no INTEGER, track_no INTEGER);
CREATE INDEX album_tracks_album ON album_tracks(album_key);
CREATE INDEX album_tracks_track ON album_tracks(track_id);
CREATE TABLE album_credits(album_key TEXT, part_norm TEXT, credit TEXT);
CREATE INDEX album_credits_part ON album_credits(part_norm, credit);
CREATE TABLE people(part_norm TEXT, role TEXT, display TEXT, album_count INTEGER, album_count_t INTEGER, sort_norm TEXT, PRIMARY KEY(part_norm, role));
CREATE VIRTUAL TABLE search USING fts5(kind UNINDEXED, ref UNINDEXED, source UNINDEXED, label, detail UNINDEXED, text, tokenize='unicode61 remove_diacritics 2');
";

    public static void Execute(SqliteConnection connection, string sql, params (string name, object? value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public static List<Dictionary<string, object?>> Query(SqliteConnection connection, string sql, params (string name, object? value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(reader.FieldCount);
            for (var column = 0; column < reader.FieldCount; column++) row[reader.GetName(column)] = reader.IsDBNull(column) ? null : reader.GetValue(column);
            rows.Add(row);
        }
        return rows;
    }

    public static string? Meta(SqliteConnection connection, string key) => Query(connection, "SELECT value FROM meta WHERE key=$key", ("$key", key)).FirstOrDefault()?["value"] as string;
}
