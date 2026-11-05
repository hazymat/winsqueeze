using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using WinSqueeze.Server;
using WinSqueeze.Settings;
using static WinSqueeze.Library.LibraryDatabase;
namespace WinSqueeze.Library;

// Mirrors the server's library into the local index. Read-only use of the server API.
public sealed partial class LibrarySync
{
    const int pageSize = 2000;
    static readonly string[] roles = { "artist", "albumartist", "trackartist", "composer", "conductor", "band" };
    public event Action<string, int, int>? Progress; // message, done, total
    public bool IsRunning { get; private set; }

    record TrackRow(long Id, string Title, int TrackNo, int DiscNo, double Duration, string Url, string Path, long TagAlbumId, int Year, string ContentType, int SampleRate, int SampleSize, string Bitrate, string CoverId, long Added, long Modified, List<(long contributorId, string role)> Contributors, List<long> GenreIds)
    {
        public long FolderId { get; set; }
    }

    [GeneratedRegex(@"^(cd|disc|disk)\s*[-_]?\s*(\d+)\b", RegexOptions.IgnoreCase)] private static partial Regex DiscFolderRegex();

    // Polls serverstatus; rebuilds only when the scan time or totals changed (or when forced)
    public async Task<bool> SyncIfChangedAsync(ServerProfile profile, LibrarySettings librarySettings, bool force)
    {
        if (IsRunning) return false;
        IsRunning = true;
        try
        {
            using var lyrionClient = new LyrionClient(profile);
            var serverStatus = await lyrionClient.GetServerStatusAsync();
            var signature = $"{serverStatus.LastScan}|{serverStatus.TotalSongs}|{serverStatus.TotalAlbums}|{serverStatus.TotalArtists}|{librarySettings.NameSeparators}|derive2";
            var databasePath = DatabasePath(profile);
            if (!force && File.Exists(databasePath))
            {
                using var existing = Open(databasePath, true);
                if (Meta(existing, "signature") == signature) return false;
            }
            await Task.Run(() => RebuildAsync(lyrionClient, databasePath, signature, serverStatus.TotalSongs, librarySettings));
            return true;
        }
        finally { IsRunning = false; }
    }

    // Builds into a side file and swaps it in, so browsing keeps working from the old index meanwhile
    async Task RebuildAsync(LyrionClient lyrionClient, string databasePath, string signature, int expectedTracks, LibrarySettings librarySettings)
    {
        var buildPath = databasePath + ".building";
        if (File.Exists(buildPath)) File.Delete(buildPath);
        using (var connection = Open(buildPath))
        {
            Execute(connection, Schema);
            var contributorNames = new Dictionary<long, string>();
            var genreNames = new Dictionary<long, string>();
            var tracks = new List<TrackRow>();
            var totalTracks = expectedTracks;
            for (var pageStart = 0; pageStart < Math.Max(totalTracks, 1); pageStart += pageSize)
            {
                Progress?.Invoke("Fetching tracks", pageStart, totalTracks);
                var page = await lyrionClient.RequestAsync("", "titles", pageStart, pageSize, "tags:acdegGiIjlnoPrsStTuyDA");
                if (page.ValueKind != JsonValueKind.Object) break;
                totalTracks = LyrionClient.ReadInt(page, "count");
                if (!page.TryGetProperty("titles_loop", out var titlesLoop)) break;
                foreach (var item in titlesLoop.EnumerateArray()) tracks.Add(ParseTrack(item, contributorNames, genreNames));
            }
            Progress?.Invoke("Fetching albums", 0, 0);
            var tagAlbums = await FetchTagAlbumsAsync(lyrionClient);
            await FillMissingNamesAsync(lyrionClient, tracks, contributorNames);
            Progress?.Invoke("Building index", tracks.Count, tracks.Count);
            using (var transaction = connection.BeginTransaction())
            {
                WriteRaw(connection, tracks, contributorNames, genreNames, librarySettings);
                Derive(connection, tracks, tagAlbums, contributorNames, librarySettings);
                Execute(connection, "INSERT INTO meta VALUES('signature',$signature),('synced_at',$syncedAt),('track_count',$trackCount)", ("$signature", signature), ("$syncedAt", DateTime.UtcNow.ToString("O")), ("$trackCount", tracks.Count.ToString()));
                transaction.Commit();
            }
        }
        File.Move(buildPath, databasePath, true);
        Progress?.Invoke("Done", 1, 1);
    }

    static TrackRow ParseTrack(JsonElement item, Dictionary<long, string> contributorNames, Dictionary<long, string> genreNames)
    {
        var url = LyrionClient.ReadString(item, "url");
        var path = url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? Uri.UnescapeDataString(new Uri(url).AbsolutePath) : url;
        var contributors = new List<(long contributorId, string role)>();
        foreach (var role in roles)
        {
            var idText = LyrionClient.ReadString(item, role + "_ids");
            if (idText == "" && role == "artist") idText = LyrionClient.ReadString(item, "artist_id");
            if (idText == "") continue;
            var contributorIds = idText.Split(',').Select(idPart => long.TryParse(idPart.Trim(), out var contributorId) ? contributorId : 0).Where(contributorId => contributorId > 0).ToList();
            var names = LyrionClient.ReadString(item, role);
            // Names come comma-joined; pair them with ids only when the counts agree, otherwise look the name up later
            var nameParts = contributorIds.Count == 1 ? new[] { names } : names.Split(", ");
            for (var index = 0; index < contributorIds.Count; index++)
            {
                contributors.Add((contributorIds[index], role));
                if (nameParts.Length == contributorIds.Count && nameParts[index] != "") contributorNames.TryAdd(contributorIds[index], nameParts[index]);
            }
        }
        var genreIdText = LyrionClient.ReadString(item, "genre_ids");
        if (genreIdText == "") genreIdText = LyrionClient.ReadString(item, "genre_id");
        var genreIds = genreIdText.Split(',').Select(idPart => long.TryParse(idPart.Trim(), out var genreId) ? genreId : 0).Where(genreId => genreId > 0).ToList();
        var genreText = LyrionClient.ReadString(item, "genres");
        if (genreText == "") genreText = LyrionClient.ReadString(item, "genre");
        var genreParts = genreIds.Count == 1 ? new[] { genreText } : genreText.Split(", ");
        if (genreParts.Length == genreIds.Count) for (var index = 0; index < genreIds.Count; index++) genreNames.TryAdd(genreIds[index], genreParts[index]);
        return new TrackRow(LyrionClient.ReadLong(item, "id"), LyrionClient.ReadString(item, "title"), LyrionClient.ReadInt(item, "tracknum"), LyrionClient.ReadInt(item, "disc"), LyrionClient.ReadDouble(item, "duration"), url, path,
            LyrionClient.ReadLong(item, "album_id"), LyrionClient.ReadInt(item, "year"), LyrionClient.ReadString(item, "type"), LyrionClient.ReadInt(item, "samplerate"), LyrionClient.ReadInt(item, "samplesize"), LyrionClient.ReadString(item, "bitrate"),
            LyrionClient.ReadString(item, "coverid"), LyrionClient.ReadLong(item, "addedTime"), LyrionClient.ReadLong(item, "modificationTime"), contributors, genreIds);
    }

    record TagAlbum(long Id, string Title, string Artist, int Year);

    static async Task<Dictionary<long, TagAlbum>> FetchTagAlbumsAsync(LyrionClient lyrionClient)
    {
        var tagAlbums = new Dictionary<long, TagAlbum>();
        for (var pageStart = 0; ; pageStart += pageSize)
        {
            var page = await lyrionClient.RequestAsync("", "albums", pageStart, pageSize, "tags:lya");
            if (page.ValueKind != JsonValueKind.Object || !page.TryGetProperty("albums_loop", out var albumsLoop)) break;
            var pageCount = 0;
            foreach (var item in albumsLoop.EnumerateArray()) { pageCount++; var albumId = LyrionClient.ReadLong(item, "id"); tagAlbums[albumId] = new TagAlbum(albumId, LyrionClient.ReadString(item, "album"), LyrionClient.ReadString(item, "artist"), LyrionClient.ReadInt(item, "year")); }
            if (pageCount < pageSize) break;
        }
        return tagAlbums;
    }

    static async Task FillMissingNamesAsync(LyrionClient lyrionClient, List<TrackRow> tracks, Dictionary<long, string> contributorNames)
    {
        foreach (var missingId in tracks.SelectMany(track => track.Contributors).Select(credit => credit.contributorId).Distinct().Where(contributorId => !contributorNames.ContainsKey(contributorId)).ToList())
        {
            try
            {
                var lookup = await lyrionClient.RequestAsync("", "artists", 0, 1, $"artist_id:{missingId}");
                if (lookup.ValueKind == JsonValueKind.Object && lookup.TryGetProperty("artists_loop", out var artistsLoop)) foreach (var item in artistsLoop.EnumerateArray()) contributorNames[missingId] = LyrionClient.ReadString(item, "artist");
            }
            catch { contributorNames[missingId] = $"Artist {missingId}"; }
        }
    }

    static void WriteRaw(SqliteConnection connection, List<TrackRow> tracks, Dictionary<long, string> contributorNames, Dictionary<long, string> genreNames, LibrarySettings librarySettings)
    {
        foreach (var (contributorId, contributorName) in contributorNames)
        {
            Execute(connection, "INSERT INTO contributors VALUES($id,$name,$norm)", ("$id", contributorId), ("$name", contributorName), ("$norm", TextNormaliser.Normalise(contributorName)));
            var position = 0;
            foreach (var part in TextNormaliser.SplitNames(contributorName, librarySettings.NameSeparators)) Execute(connection, "INSERT INTO contributor_parts VALUES($id,$norm,$display,$position)", ("$id", contributorId), ("$norm", TextNormaliser.Normalise(part)), ("$display", part), ("$position", position++));
        }
        foreach (var (genreId, genreName) in genreNames) Execute(connection, "INSERT INTO genres VALUES($id,$name,$norm)", ("$id", genreId), ("$name", genreName), ("$norm", TextNormaliser.Normalise(genreName)));
        // Folder tree from file paths, rooted at the common music folder
        var folderIds = new Dictionary<string, long>();
        var filePaths = tracks.Where(track => track.Path.StartsWith('/') || track.Path.Contains(':')).Select(track => ParentOf(track.Path)).ToList();
        var rootPath = filePaths.Count == 0 ? "" : CommonPrefix(filePaths);
        long EnsureFolder(string folderPath)
        {
            if (folderIds.TryGetValue(folderPath, out var existingId)) return existingId;
            long? parentId = folderPath.Length <= rootPath.Length ? null : EnsureFolder(ParentOf(folderPath));
            var folderName = folderPath.Length <= rootPath.Length ? "Music" : folderPath[(folderPath.LastIndexOf('/') + 1)..];
            Execute(connection, "INSERT INTO folders(path,parent_id,name,name_norm) VALUES($path,$parent,$name,$norm)", ("$path", folderPath), ("$parent", parentId), ("$name", folderName), ("$norm", TextNormaliser.Normalise(folderName)));
            var newId = (long)Query(connection, "SELECT last_insert_rowid() AS id")[0]["id"]!;
            folderIds[folderPath] = newId;
            return newId;
        }
        using var insertTrack = connection.CreateCommand();
        insertTrack.CommandText = "INSERT INTO tracks VALUES($id,$title,$titleNorm,$trackNo,$discNo,$duration,$url,$folder,$album,$year,$type,$rate,$size,$bitrate,$cover,$added,$modified,$albumArtist,$trackArtist,$composer)";
        foreach (var parameterName in new[] { "$id", "$title", "$titleNorm", "$trackNo", "$discNo", "$duration", "$url", "$folder", "$album", "$year", "$type", "$rate", "$size", "$bitrate", "$cover", "$added", "$modified", "$albumArtist", "$trackArtist", "$composer" }) insertTrack.Parameters.Add(new SqliteParameter(parameterName, DBNull.Value));
        using var insertCredit = connection.CreateCommand();
        insertCredit.CommandText = "INSERT INTO track_contributors VALUES($track,$contributor,$role)";
        insertCredit.Parameters.Add(new SqliteParameter("$track", 0L)); insertCredit.Parameters.Add(new SqliteParameter("$contributor", 0L)); insertCredit.Parameters.Add(new SqliteParameter("$role", ""));
        string NamesFor(TrackRow track, params string[] wantedRoles) => string.Join(", ", track.Contributors.Where(credit => wantedRoles.Contains(credit.role)).Select(credit => contributorNames.GetValueOrDefault(credit.contributorId, "")).Where(name => name != "").Distinct());
        foreach (var track in tracks)
        {
            track.FolderId = rootPath != "" && track.Path.StartsWith(rootPath) ? EnsureFolder(ParentOf(track.Path)) : 0;
            var albumArtist = NamesFor(track, "albumartist");
            if (albumArtist == "") albumArtist = NamesFor(track, "artist");
            var trackValues = new object?[] { track.Id, track.Title, TextNormaliser.Normalise(track.Title), track.TrackNo, track.DiscNo, track.Duration, track.Url, track.FolderId, track.TagAlbumId, track.Year, track.ContentType, track.SampleRate, track.SampleSize, track.Bitrate, track.CoverId, track.Added, track.Modified, albumArtist, NamesFor(track, "trackartist", "artist"), NamesFor(track, "composer") };
            for (var index = 0; index < trackValues.Length; index++) insertTrack.Parameters[index].Value = trackValues[index] ?? DBNull.Value;
            insertTrack.ExecuteNonQuery();
            foreach (var (contributorId, role) in track.Contributors.Distinct()) { insertCredit.Parameters[0].Value = track.Id; insertCredit.Parameters[1].Value = contributorId; insertCredit.Parameters[2].Value = role; insertCredit.ExecuteNonQuery(); }
            foreach (var genreId in track.GenreIds.Distinct()) Execute(connection, "INSERT INTO track_genres VALUES($track,$genre)", ("$track", track.Id), ("$genre", genreId));
        }
        Execute(connection, "UPDATE folders SET track_count=(SELECT COUNT(*) FROM tracks WHERE tracks.folder_id=folders.id)");
        Execute(connection, "INSERT INTO meta VALUES('root_path',$root)", ("$root", rootPath));
    }

    // Albums for both sources (tags and folders), credits for artist/composer/performer pages, people lists and the search index
    static void Derive(SqliteConnection connection, List<TrackRow> tracks, Dictionary<long, TagAlbum> tagAlbums, Dictionary<long, string> contributorNames, LibrarySettings librarySettings)
    {
        var folderRows = Query(connection, "SELECT id, path, parent_id, name FROM folders").ToDictionary(row => (long)row["id"]!);
        var albumGroups = new Dictionary<string, List<(TrackRow track, int discNo)>>();
        void AddToAlbum(string albumKey, TrackRow track, int discNo) { if (!albumGroups.TryGetValue(albumKey, out var members)) albumGroups[albumKey] = members = new(); members.Add((track, discNo)); }
        foreach (var track in tracks)
        {
            if (track.TagAlbumId > 0) AddToAlbum("t:" + track.TagAlbumId, track, track.DiscNo);
            if (track.FolderId == 0) continue;
            // CD1/CD2 subfolders belong to the parent folder's album
            var albumFolderId = track.FolderId;
            var discNo = track.DiscNo;
            var discMatch = DiscFolderRegex().Match((string)folderRows[track.FolderId]["name"]!);
            if (discMatch.Success && folderRows[track.FolderId]["parent_id"] is long parentFolderId) { albumFolderId = parentFolderId; if (discNo == 0) discNo = int.Parse(discMatch.Groups[2].Value); }
            AddToAlbum("f:" + albumFolderId, track, discNo);
        }
        var albumArtistByTrack = Query(connection, "SELECT id, album_artist FROM tracks").ToDictionary(row => (long)row["id"]!, row => row["album_artist"] as string ?? "");
        var people = new Dictionary<(string partNorm, string role), (string display, HashSet<string> folderAlbums, HashSet<string> tagAlbums)>();
        void AddPerson(string display, string role, string albumKey)
        {
            var personKey = (TextNormaliser.Normalise(display), role);
            if (personKey.Item1 == "") return;
            if (!people.TryGetValue(personKey, out var person)) people[personKey] = person = (display, new(), new());
            (albumKey.StartsWith("f:") ? person.folderAlbums : person.tagAlbums).Add(albumKey);
        }
        foreach (var (albumKey, members) in albumGroups)
        {
            var orderedMembers = members.OrderBy(member => member.discNo).ThenBy(member => member.track.TrackNo).ThenBy(member => member.track.Path).ToList();
            var isFolderAlbum = albumKey.StartsWith("f:");
            var folderId = isFolderAlbum ? long.Parse(albumKey[2..]) : orderedMembers[0].track.FolderId;
            // Album artist: the tag album's own, or the dominant one among the folder's tracks
            var artistCounts = orderedMembers.GroupBy(member => albumArtistByTrack[member.track.Id]).OrderByDescending(group => group.Count()).ToList();
            var albumArtist = !isFolderAlbum && tagAlbums.TryGetValue(orderedMembers[0].track.TagAlbumId, out var tagAlbum) && tagAlbum.Artist != "" ? tagAlbum.Artist : artistCounts.Count > 0 && artistCounts[0].Count() * 2 >= orderedMembers.Count ? artistCounts[0].Key : "Various Artists";
            var title = isFolderAlbum ? (string)folderRows[folderId]["name"]! : tagAlbums.TryGetValue(orderedMembers[0].track.TagAlbumId, out var titledAlbum) ? titledAlbum.Title : "Unknown album";
            var formatGroups = orderedMembers.GroupBy(member => FormatSummary(member.track)).OrderByDescending(group => group.Count()).ToList();
            var format = formatGroups.Count == 1 ? formatGroups[0].Key : formatGroups[0].Key + " (mixed)";
            Execute(connection, "INSERT INTO albums VALUES($key,$source,$title,$titleNorm,$artist,$artistNorm,$year,$cover,$count,$duration,$added,$folder,$format)",
                ("$key", albumKey), ("$source", albumKey[..1]), ("$title", title), ("$titleNorm", TextNormaliser.Normalise(title)), ("$artist", albumArtist), ("$artistNorm", TextNormaliser.Normalise(albumArtist)),
                ("$year", orderedMembers.Max(member => member.track.Year)), ("$cover", orderedMembers.Select(member => member.track.CoverId).FirstOrDefault(coverId => coverId != "")), ("$count", orderedMembers.Count),
                ("$duration", orderedMembers.Sum(member => member.track.Duration)), ("$added", orderedMembers.Max(member => member.track.Added)), ("$folder", folderId), ("$format", format));
            if (isFolderAlbum) Execute(connection, "UPDATE folders SET album_key=$key WHERE id=$id", ("$key", albumKey), ("$id", folderId));
            foreach (var (track, discNo) in orderedMembers) Execute(connection, "INSERT INTO album_tracks VALUES($key,$track,$disc,$trackNo)", ("$key", albumKey), ("$track", track.Id), ("$disc", discNo), ("$trackNo", track.TrackNo));
            // Credits: first album-artist name is the album's own artist, the rest are 'with'; track-level roles follow
            var credits = new HashSet<(string partNorm, string credit)>();
            var albumArtistParts = TextNormaliser.SplitNames(albumArtist, librarySettings.NameSeparators);
            for (var index = 0; index < albumArtistParts.Count; index++) { credits.Add((TextNormaliser.Normalise(albumArtistParts[index]), index == 0 ? "own" : "with")); AddPerson(albumArtistParts[index], "artist", albumKey); }
            var trackArtistParts = new List<string>();
            foreach (var (track, discNo) in orderedMembers)
                foreach (var (contributorId, role) in track.Contributors)
                {
                    var credit = role switch { "composer" => "composer", "conductor" => "conductor", "band" => "band", _ => "track" };
                    foreach (var part in TextNormaliser.SplitNames(contributorNames.GetValueOrDefault(contributorId, ""), librarySettings.NameSeparators))
                    {
                        credits.Add((TextNormaliser.Normalise(part), credit));
                        AddPerson(part, credit == "track" ? "artist" : credit, albumKey);
                        if (role == "trackartist") trackArtistParts.Add(part);
                    }
                }
            var classicalAlbum = credits.Any(credit => credit.credit is "conductor" or "band");
            var albumArtistNorms = albumArtistParts.Select(TextNormaliser.Normalise).ToHashSet();
            if (classicalAlbum) foreach (var part in trackArtistParts.Distinct()) if (!albumArtistNorms.Contains(TextNormaliser.Normalise(part))) AddPerson(part, "soloist", albumKey);
            foreach (var (partNorm, credit) in credits) Execute(connection, "INSERT INTO album_credits VALUES($key,$part,$credit)", ("$key", albumKey), ("$part", partNorm), ("$credit", credit));
            Execute(connection, "INSERT INTO search(kind,ref,source,label,detail,text) VALUES('album',$key,$source,$title,$artist,$text)", ("$key", albumKey), ("$source", albumKey[..1]), ("$title", title), ("$artist", albumArtist), ("$text", title + " " + albumArtist));
        }
        foreach (var ((partNorm, role), person) in people)
        {
            Execute(connection, "INSERT INTO people(part_norm,role,display,album_count,album_count_t) VALUES($part,$role,$display,$countFolders,$countTags)", ("$part", partNorm), ("$role", role), ("$display", person.display), ("$countFolders", person.folderAlbums.Count), ("$countTags", person.tagAlbums.Count));
            var searchKind = role switch { "artist" => "artist", "composer" => "composer", _ => "performer" };
            if (role != "soloist") Execute(connection, "INSERT INTO search(kind,ref,source,label,detail,text) VALUES($kind,$part,'',$display,$role,$display)", ("$kind", searchKind), ("$part", partNorm), ("$display", person.display), ("$role", role));
        }
        Execute(connection, "INSERT INTO search(kind,ref,source,label,detail,text) SELECT 'track', id, '', title, album_artist, title || ' ' || COALESCE(composer,'') || ' ' || COALESCE(track_artist,'') FROM tracks");
    }

    static string FormatSummary(TrackRow track)
    {
        var codec = track.ContentType.ToUpperInvariant() switch { "FLC" => "FLAC", "ALC" => "ALAC", "AIF" => "AIFF", "MP3" => "MP3", "WAV" => "WAV", "" => "?", var other => other };
        return track.SampleRate > 0 ? $"{codec} {(track.SampleSize > 0 ? track.SampleSize + "/" : "")}{track.SampleRate / 1000.0:0.#}" : codec;
    }

    static string ParentOf(string path) { var slashIndex = path.TrimEnd('/').LastIndexOf('/'); return slashIndex <= 0 ? path : path[..slashIndex]; }

    static string CommonPrefix(List<string> folderPaths)
    {
        var prefix = folderPaths[0];
        foreach (var folderPath in folderPaths) while (prefix.Length > 0 && !(folderPath == prefix || folderPath.StartsWith(prefix + "/"))) prefix = ParentOf(prefix) == prefix ? "" : ParentOf(prefix);
        return prefix;
    }
}
