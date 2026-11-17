using Mp3TagUpdater.Models;

namespace Mp3TagUpdater.Services;

/// <summary>
/// Handles scanning a directory for mp3 files, reading their tags into
/// Mp3TagRecord instances, repairing files that can't be read, and updating
/// tags and file names: the Album from the folder name when it is missing,
/// misencoded letters, and the artist held in the title.
/// </summary>
/// <param name="onMessage">
/// Receives a line for each proposed or actual change and each failed write.
/// It is called on the thread doing the work.
/// </param>
public class Mp3Library(Action<Mp3Message>? onMessage = null)
{
    /// <summary>
    /// Reads every mp3 in <see cref="Mp3RunOptions.Directory"/>, repairs unreadable
    /// files, and then updates tags and names: misencoded letters, artist in the title and
    /// missing albums, as the options ask. Repairs come first so that a repaired file can
    /// also be updated. A cancelled run stops before the next file, and the
    /// files it didn't reach keep the record they had at that point.
    /// </summary>
    public Mp3RunResult Run(Mp3RunOptions options, IProgress<Mp3Progress>? progress = null, CancellationToken cancellation = default)
    {
        var records = ReadTags(options.Directory, options.Recursive, progress, cancellation);
        records = RepairUnreadableFiles(records, options.DryRun, progress, cancellation);
        records = UpdateTags(records, options, progress, cancellation);
        if (options.FixNames)
            records = FixNames(records, options, progress, cancellation);
        return new Mp3RunResult(records, cancellation.IsCancellationRequested);
    }

    /// <summary>
    /// Recursively (or top-level only) scans <paramref name="rootDirectory"/> for
    /// .mp3 files and reads their tag data into a list of records. A file that
    /// can't be read still gets a record, with its status set to Error.
    /// </summary>
    public List<Mp3TagRecord> ReadTags(string rootDirectory, bool recursive = true,
        IProgress<Mp3Progress>? progress = null, CancellationToken cancellation = default)
    {
        if (!Directory.Exists(rootDirectory))
            throw new DirectoryNotFoundException($"Directory not found: {rootDirectory}");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        progress?.Report(new Mp3Progress("Finding mp3 files", 0, 0));
        var mp3Files = Directory.EnumerateFiles(rootDirectory, "*.mp3", searchOption).ToList();

        var records = new List<Mp3TagRecord>();

        foreach (var filePath in mp3Files)
        {
            if (cancellation.IsCancellationRequested)
                break;

            try
            {
                records.Add(ReadSingleFile(filePath));
            }
            catch (Exception ex)
            {
                records.Add(CreateFileRecord(filePath) with
                {
                    Status = Mp3Status.Error,
                    Errors = [ex.Message]
                });
            }

            progress?.Report(new Mp3Progress("Reading tags", records.Count, mp3Files.Count));
        }

        return records;
    }

    /// <summary>
    /// Attempts to repair every record with Error status. Each repair is first
    /// verified in memory. The file is only changed if <paramref name="dryRun"/>
    /// is false, and the original is then kept alongside it as .bak.
    /// </summary>
    public List<Mp3TagRecord> RepairUnreadableFiles(List<Mp3TagRecord> records, bool dryRun = true,
        IProgress<Mp3Progress>? progress = null, CancellationToken cancellation = default) =>
        Process(records, "Repairing", r => r.Status == Mp3Status.Error, r => RepairFile(r, dryRun), progress, cancellation);

    private Mp3TagRecord RepairFile(Mp3TagRecord record, bool dryRun)
    {
        try
        {
            var repair = Id3v2Repair.TryRepair(File.ReadAllBytes(record.FilePath));
            if (repair is null)
                return record;

            // Throws if TagLib still can't read the repaired data.
            var verified = ReadFromMemory(record.FilePath, repair.Data);

            Report(Mp3MessageKind.Change, dryRun
                ? $"[DRY RUN] Would repair {record.FileName}: {string.Join("; ", repair.Actions)}"
                : $"Repairing {record.FileName}: {string.Join("; ", repair.Actions)}");

            if (dryRun)
            {
                return verified with
                {
                    Status = Mp3Status.Repairable,
                    Errors = record.Errors,
                    Changes = repair.Actions
                };
            }

            ReplaceKeepingBackup(record.FilePath, repair.Data);

            return ReadSingleFile(record.FilePath) with
            {
                Status = Mp3Status.Repaired,
                Errors = record.Errors,
                Changes = repair.Actions
            };
        }
        catch (Exception ex)
        {
            // A dry run changes nothing, so its failures belong only in the log.
            if (!dryRun)
                Report(Mp3MessageKind.Error, $"Failed to repair '{record.FilePath}': {ex.Message}");

            return record with { Errors = [.. record.Errors, $"Repair failed: {ex.Message}"] };
        }
    }

    /// <summary>
    /// Works out each file's new tags and name (see <see cref="PlanEdits"/>) and writes them.
    /// Files that are unreadable or flagged with a warning are skipped, so that tags are never
    /// rewritten on a file that may be damaged or not really an mp3.
    /// </summary>
    public List<Mp3TagRecord> UpdateTags(List<Mp3TagRecord> records, Mp3RunOptions options,
        IProgress<Mp3Progress>? progress = null, CancellationToken cancellation = default)
    {
        var plans = PlanEdits(records, options);
        return Process(records, "Updating tags", r => plans.ContainsKey(r.FilePath),
            r => ApplyEdit(r, plans[r.FilePath], options.DryRun), progress, cancellation);
    }

    /// <summary>
    /// Applies <paramref name="change"/> to each record it is meant for, in order.
    /// After cancellation the remaining records are returned as they are.
    /// </summary>
    private static List<Mp3TagRecord> Process(List<Mp3TagRecord> records, string phase,
        Func<Mp3TagRecord, bool> applies, Func<Mp3TagRecord, Mp3TagRecord> change,
        IProgress<Mp3Progress>? progress, CancellationToken cancellation)
    {
        var result = new List<Mp3TagRecord>(records.Count);
        var total = records.Count(applies);
        var done = 0;

        foreach (var record in records)
        {
            if (cancellation.IsCancellationRequested || !applies(record))
            {
                result.Add(record);
                continue;
            }

            result.Add(change(record));
            progress?.Report(new Mp3Progress(phase, ++done, total));
        }

        return result;
    }

    private static bool CanEdit(Mp3TagRecord record) =>
        record.Status is Mp3Status.Ok or Mp3Status.Repairable or Mp3Status.Repaired;

    /// <summary>
    /// Works out the new tags and name of every file that needs them, keyed by path:
    /// <list type="number">
    /// <item>With <see cref="Mp3RunOptions.FixLetters"/>, misencoded letters in Title, Artist, Album
    /// and Album Artist are corrected, using the file name to recover lost letters.</item>
    /// <item>With <see cref="Mp3RunOptions.SplitTitles"/>, a title such as "Artist / Title" is split
    /// into Artist and Title. The order is decided per folder unless the mode fixes it.</item>
    /// <item>With <see cref="Mp3RunOptions.FillFromFileName"/>, a missing Title, Artist, Track and Disc
    /// are read from the file name. See <see cref="FileNameTags"/>.</item>
    /// <item>A missing Album is set to the folder name. With <see cref="Mp3RunOptions.ReplaceAlbum"/>,
    /// an Album that is set is replaced by the folder name too.</item>
    /// <item>With <see cref="Mp3RunOptions.FixLetters"/>, misencoded letters in the file name are
    /// corrected, trusting the corrected tags where they spell a word differently.</item>
    /// </list>
    /// </summary>
    public static Dictionary<string, (Mp3TagEdit Edit, IReadOnlyList<string> Changes, IReadOnlyList<string> Warnings)> PlanEdits(
        IReadOnlyList<Mp3TagRecord> records, Mp3RunOptions options)
    {
        var editable = records.Where(CanEdit).ToList();

        // Step 1: letters. Every later step works on the corrected text.
        var texts = editable.ToDictionary(r => r.FilePath, r =>
        {
            var reference = Path.GetFileNameWithoutExtension(r.FileName);
            var warnings = new List<string>();
            var fixedAny = false;

            string? FixTag(string field, string? value, string? reference)
            {
                if (!options.FixLetters || MisencodedText.Fix(value, [reference]) is not { } fix)
                    return value;
                fixedAny = true;
                if (fix.Unresolved > 0)
                    warnings.Add($"{field}: couldn't tell which letter \"Ã \" stands for in \"{value}\"");
                return fix.Text;
            }

            var title = FixTag("Title", r.Title, reference);
            var artist = FixTag("Artist", r.Artist, reference);
            var album = FixTag("Album", r.Album, r.ContainingDirectoryName);
            var albumArtist = FixTag("Album Artist", r.AlbumArtist, null);
            return (Title: title, Artist: artist, Album: album, AlbumArtist: albumArtist, Fixed: fixedAny, Warnings: warnings);
        });

        // Step 2: which way round each folder's "a / b" titles are.
        var artistFirst = options.SplitTitles == TitleSplitMode.Auto
            ? ArtistTitleSplit.ArtistFirstByFolder(editable.Select(r =>
                (Path.GetDirectoryName(r.FilePath) ?? "", texts[r.FilePath].Artist, texts[r.FilePath].Title)))
            : [];

        // Step 3 needs to know which folders number their files, so "50 Cent - ..." isn't track 50.
        var numberedFolders = editable
            .GroupBy(r => Path.GetDirectoryName(r.FilePath) ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Count(r => FileNameTags.StartsWithNumber(Path.GetFileNameWithoutExtension(r.FileName))) * 2 > g.Count(),
                StringComparer.OrdinalIgnoreCase);

        var plans = new Dictionary<string, (Mp3TagEdit, IReadOnlyList<string>, IReadOnlyList<string>)>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in editable)
        {
            var text = texts[record.FilePath];
            var (title, artist) = (text.Title, text.Artist);
            var split = false;

            if (options.SplitTitles != TitleSplitMode.Off && ArtistTitleSplit.Split(title) is { } halves)
            {
                var first = options.SplitTitles switch
                {
                    TitleSplitMode.ArtistFirst => true,
                    TitleSplitMode.TitleFirst => false,
                    _ => artistFirst.GetValueOrDefault(Path.GetDirectoryName(record.FilePath) ?? "", true)
                };
                (artist, title) = ArtistTitleSplit.Assign(halves, first);
                split = true;
            }

            // Step 3: what the file name says, for the tags that are still empty.
            uint? track = null, disc = null;
            var fromName = false;
            if (options.FillFromFileName && FileNameTags.Parse(ReadableStem(record.FileName, options),
                    numberedFolders.GetValueOrDefault(Path.GetDirectoryName(record.FilePath) ?? "")) is { } info)
            {
                if (string.IsNullOrWhiteSpace(title) && info.Title is not null)
                    (title, fromName) = (info.Title, true);
                if (string.IsNullOrWhiteSpace(artist) && info.Artist is not null)
                    (artist, fromName) = (info.Artist, true);
                if (record.Track == 0 && info.Track > 0)
                    (track, fromName) = (info.Track, true);
                if (record.Disc == 0 && info.Disc > 0)
                    (disc, fromName) = (info.Disc, true);
            }

            var album = text.Album;
            var fillsAlbum = record.AlbumWasMissing;
            var folderAlbum = options.FixNames ? FixedName(record.ContainingDirectoryName) : record.ContainingDirectoryName;
            if (fillsAlbum)
                album = folderAlbum;

            // An album that is set is replaced only when the option asks, and only when it differs.
            var replacesAlbum = options.ReplaceAlbum && !fillsAlbum && !string.Equals(record.Album, folderAlbum, StringComparison.Ordinal);
            if (replacesAlbum)
                album = folderAlbum;

            // Step 4: the file name, checked against the corrected title and artist.
            string? fileName = null;
            var stem = Path.GetFileNameWithoutExtension(record.FileName);
            if (options.FixLetters && MisencodedText.Fix(stem, [title, artist, album], trustReferences: true) is { } nameFix)
            {
                fileName = nameFix.Text + Path.GetExtension(record.FileName);
                if (nameFix.Unresolved > 0)
                    text.Warnings.Add($"File name: couldn't tell which letter \"Ã \" stands for in \"{record.FileName}\"");
            }

            var edit = new Mp3TagEdit
            {
                Title = Changed(record.Title, title),
                Artist = Changed(record.Artist, artist),
                Album = Changed(record.Album, album),
                AlbumArtist = Changed(record.AlbumArtist, text.AlbumArtist),
                Track = track,
                Disc = disc,
                FileName = fileName,
                FixesLetters = text.Fixed || fileName is not null,
                SplitsTitle = split,
                FillsAlbum = fillsAlbum,
                ReplacesAlbum = replacesAlbum,
                FillsFromFileName = fromName
            };

            if (!edit.ChangesTags && edit.FileName is null)
            {
                if (text.Warnings.Count > 0)
                    plans[record.FilePath] = (edit, [], text.Warnings);
                continue;
            }

            var changes = new List<string>();
            if (edit.Title is not null)
                changes.Add(record.Title is null ? $"Set Title to \"{edit.Title}\"" : $"Title \"{record.Title}\" → \"{edit.Title}\"");
            if (edit.Artist is not null)
                changes.Add(record.Artist is null ? $"Set Artist to \"{edit.Artist}\"" : $"Artist \"{record.Artist}\" → \"{edit.Artist}\"");
            if (edit.Album is not null)
                changes.Add(fillsAlbum ? $"Set Album to \"{edit.Album}\"" : $"Album \"{record.Album}\" → \"{edit.Album}\"");
            if (edit.AlbumArtist is not null)
                changes.Add($"Album Artist \"{record.AlbumArtist}\" → \"{edit.AlbumArtist}\"");
            if (edit.Disc is not null)
                changes.Add($"Set Disc to {edit.Disc}");
            if (edit.Track is not null)
                changes.Add($"Set Track to {edit.Track}");
            if (edit.FileName is not null)
                changes.Add($"Rename to \"{edit.FileName}\"");

            plans[record.FilePath] = (edit, changes, text.Warnings);
        }

        return plans;

        static string? Changed(string? before, string? after) =>
            after is not null && !string.Equals(before, after, StringComparison.Ordinal) ? after : null;
    }

    /// <summary>The file name without its extension, with misencoded letters corrected as the options ask.</summary>
    private static string ReadableStem(string fileName, Mp3RunOptions options)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (options.FixNames)
            stem = FixedName(stem);
        if (options.FixLetters && MisencodedText.Fix(stem, []) is { Unresolved: 0 } fix)
            stem = fix.Text;
        return stem;
    }

    private Mp3TagRecord ApplyEdit(Mp3TagRecord record,
        (Mp3TagEdit Edit, IReadOnlyList<string> Changes, IReadOnlyList<string> Warnings) plan, bool dryRun)
    {
        var (edit, newChanges, warnings) = plan;
        IReadOnlyList<string> errors = [.. record.Errors, .. warnings];
        if (newChanges.Count == 0)
            return record with { Errors = errors };

        IReadOnlyList<string> changes = [.. record.Changes, .. newChanges];
        var summary = string.Join("; ", newChanges);

        if (dryRun)
        {
            Report(Mp3MessageKind.Change, $"[DRY RUN] Would change {record.FileName}: {summary}");
            return record with
            {
                Status = record.Status == Mp3Status.Ok ? Mp3Status.Updatable : record.Status,
                Errors = errors,
                Changes = changes,
                Edit = edit
            };
        }

        try
        {
            var path = record.FilePath;
            if (edit.ChangesTags)
                WriteTags(path, edit);
            if (edit.FileName is not null)
                path = Rename(path, edit.FileName);

            Report(Mp3MessageKind.Change, $"Changed {record.FileName}: {summary}");

            return ReadSingleFile(path) with
            {
                Status = record.Status == Mp3Status.Repaired ? Mp3Status.Repaired : Mp3Status.Updated,
                Errors = errors,
                Changes = changes,
                Edit = edit
            };
        }
        catch (Exception ex)
        {
            Report(Mp3MessageKind.Error, $"Failed to update '{record.FilePath}': {ex.Message}");
            return record with
            {
                Status = Mp3Status.Error,
                Errors = [.. errors, $"Update failed: {ex.Message}"]
            };
        }
    }

    /// <summary>The name with its misencoded letters corrected, when the correction is certain.</summary>
    private static string FixedName(string name) =>
        MojibakeText.TryRepair(name) is { Certain: true } repair ? repair.Fixed : name;

    /// <summary>
    /// Corrects misencoded letters in the name of every file and folder below the directory
    /// (the directory itself keeps its name), like MojibakeFileFix. An mp3 gets the rename added
    /// to its record, unless the tag step already renames it. Other files and folders get a
    /// record of their own when their name needs attention. A folder's contents are renamed
    /// before the folder, and the paths of the records below it follow the rename.
    /// </summary>
    /// <remarks>
    /// A name whose only evidence is a single sequence that could be ordinary text ("MaÅ‚y") is
    /// uncertain, and is listed with a warning but not renamed. So is damage that lost a byte,
    /// such as "Ã jne". A name is never renamed onto an existing file or folder.
    /// </remarks>
    public List<Mp3TagRecord> FixNames(List<Mp3TagRecord> records, Mp3RunOptions options,
        IProgress<Mp3Progress>? progress = null, CancellationToken cancellation = default)
    {
        var result = records.ToList();
        if (cancellation.IsCancellationRequested)
            return result;

        progress?.Report(new Mp3Progress("Checking names", 0, 0));
        var findings = new List<(string Path, bool IsDirectory, MojibakeRepair? Repair, IReadOnlyList<string> Warnings)>();
        CollectNames(new DirectoryInfo(Path.GetFullPath(options.Directory)), options.Recursive, findings, cancellation);

        var done = 0;
        foreach (var (path, isDirectory, repair, warnings) in findings)
        {
            if (cancellation.IsCancellationRequested)
                break;

            var index = isDirectory ? -1 : result.FindIndex(r => r.Kind == Mp3EntryKind.Mp3 && SamePath(r.FilePath, path));
            var record = index >= 0 ? result[index] : CreateEntryRecord(path, isDirectory);

            // The tag step already renames this mp3, with the corrected tags to go by.
            if (options.DryRun && record.Edit?.FileName is not null)
                continue;

            var updated = FixName(record, repair, warnings, options.DryRun, result);
            if (index >= 0)
                result[index] = updated;
            else
                result.Add(updated);

            progress?.Report(new Mp3Progress("Fixing names", ++done, findings.Count));
        }

        return result;

        static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }

    private Mp3TagRecord FixName(Mp3TagRecord record, MojibakeRepair? repair, IReadOnlyList<string> damage,
        bool dryRun, List<Mp3TagRecord> all)
    {
        var isMp3 = record.Kind == Mp3EntryKind.Mp3;
        var what = record.Kind == Mp3EntryKind.Folder ? "folder" : "file";
        var warnings = damage.Select(d => $"Name: {d}").ToList();

        if (repair is { Certain: false })
        {
            warnings.Add($"Name may be misencoded: \"{repair.Original}\" would become \"{repair.Fixed}\", " +
                "but it could also be correct as it is. Not renamed.");
            repair = null;
        }

        // An mp3 keeps its own status; another file or folder is only listed because of its name.
        var warningStatus = isMp3 ? record.Status : Mp3Status.Warning;
        IReadOnlyList<string> errors = [.. record.Errors, .. warnings];

        if (repair is null)
            return record with { Status = warnings.Count > 0 ? warningStatus : record.Status, Errors = errors };

        var change = $"Rename {(isMp3 ? "" : what + " ")}to \"{repair.Fixed}\"";
        IReadOnlyList<string> changes = [.. record.Changes, change];
        var edit = (record.Edit ?? new Mp3TagEdit()) with { FileName = repair.Fixed, FixesLetters = true };

        if (dryRun)
        {
            Report(Mp3MessageKind.Change, $"[DRY RUN] Would rename {what} {record.FileName} to \"{repair.Fixed}\"");
            return record with
            {
                // Renaming doesn't touch the contents, so even a damaged mp3 can be renamed; it keeps its status.
                Status = !isMp3 || record.Status == Mp3Status.Ok ? Mp3Status.Updatable : record.Status,
                Errors = errors,
                Changes = changes,
                Edit = edit
            };
        }

        var oldPath = record.FilePath;
        var newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, repair.Fixed);
        try
        {
            // Windows names are case-insensitive, so "É.txt" also collides with an existing "é.txt".
            if (File.Exists(newPath) || Directory.Exists(newPath))
                throw new IOException($"\"{repair.Fixed}\" already exists.");

            if (record.Kind == Mp3EntryKind.Folder)
                Directory.Move(oldPath, newPath);
            else
                File.Move(oldPath, newPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(Mp3MessageKind.Error, $"Failed to rename '{oldPath}': {ex.Message}");
            return record with { Status = Mp3Status.Error, Errors = [.. errors, $"Rename failed: {ex.Message}"] };
        }

        Report(Mp3MessageKind.Change, $"Renamed {what} {record.FileName} to \"{repair.Fixed}\"");
        if (record.Kind == Mp3EntryKind.Folder)
            MoveRecordsBelow(all, oldPath, newPath);

        return record with
        {
            FilePath = newPath,
            FileName = repair.Fixed,
            Status = !isMp3 || record.Status is Mp3Status.Ok or Mp3Status.Updated ? Mp3Status.Updated : record.Status,
            Errors = errors,
            Changes = changes,
            Edit = edit
        };
    }

    /// <summary>Points the records inside a renamed folder at their new paths.</summary>
    private static void MoveRecordsBelow(List<Mp3TagRecord> records, string oldFolder, string newFolder)
    {
        var prefix = Path.TrimEndingDirectorySeparator(oldFolder) + Path.DirectorySeparatorChar;
        var newName = Path.GetFileName(newFolder);
        for (var i = 0; i < records.Count; i++)
        {
            var path = records[i].FilePath;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var moved = Path.Combine(newFolder, path[prefix.Length..]);
            var directlyInside = string.Equals(Path.GetDirectoryName(moved), newFolder, StringComparison.OrdinalIgnoreCase);
            records[i] = records[i] with
            {
                FilePath = moved,
                ContainingDirectoryName = directlyInside ? newName : records[i].ContainingDirectoryName
            };
        }
    }

    /// <summary>
    /// Adds every name below <paramref name="directory"/> that needs attention, a folder's contents
    /// before the folder, so they can be renamed in this order. Hidden and system entries are
    /// included. Folder links (junctions, symbolic links) are checked but not entered.
    /// </summary>
    private static void CollectNames(DirectoryInfo directory, bool recursive,
        List<(string, bool, MojibakeRepair?, IReadOnlyList<string>)> findings, CancellationToken cancellation)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = directory
                .EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true })
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (cancellation.IsCancellationRequested)
                return;

            var isDirectory = entry is DirectoryInfo;
            if (isDirectory && recursive && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                CollectNames((DirectoryInfo)entry, recursive, findings, cancellation);

            var repair = MojibakeText.TryRepair(entry.Name);
            var warnings = MojibakeText.FindUnrecoverable(repair?.Fixed ?? entry.Name);
            if (repair is not null || warnings.Count > 0)
                findings.Add((entry.FullName, isDirectory, repair, warnings));
        }
    }

    /// <summary>A record for a file or folder that isn't an mp3, holding only what the file system knows.</summary>
    private static Mp3TagRecord CreateEntryRecord(string path, bool isDirectory)
    {
        FileSystemInfo info = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
        return new Mp3TagRecord
        {
            Kind = isDirectory ? Mp3EntryKind.Folder : Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                ? Mp3EntryKind.Mp3 : Mp3EntryKind.File,
            FilePath = info.FullName,
            FileName = info.Name,
            ContainingDirectoryName = Path.GetFileName(Path.GetDirectoryName(info.FullName)) ?? "",
            SizeBytes = info is FileInfo file ? file.Length : 0,
            Created = info.CreationTime,
            Modified = info.LastWriteTime
        };
    }

    /// <summary>Renames the file within its folder, and returns the new path. An existing file is never replaced.</summary>
    private static string Rename(string filePath, string fileName)
    {
        var target = Path.Combine(Path.GetDirectoryName(filePath)!, fileName);
        if (File.Exists(target) && !string.Equals(Path.GetFullPath(target), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Can't rename to \"{fileName}\": a file with that name already exists.");
        File.Move(filePath, target);
        return target;
    }

    private void Report(Mp3MessageKind kind, string text) => onMessage?.Invoke(new Mp3Message(kind, text));

    /// <summary>
    /// Writes the tags to a temporary copy and then swaps it in, so a failed save
    /// can't damage the original. The modified time is kept because the audio
    /// hasn't changed.
    /// </summary>
    private static void WriteTags(string filePath, Mp3TagEdit edit)
    {
        var modified = File.GetLastWriteTimeUtc(filePath);
        var tempPath = filePath + ".tmp";

        try
        {
            File.Copy(filePath, tempPath, overwrite: true);

            // The .tmp extension means TagLib has to be told the file type.
            using (var file = TagLib.File.Create(tempPath, "audio/mpeg", TagLib.ReadStyle.Average))
            {
                if (edit.Title is not null)
                    file.Tag.Title = edit.Title;
                if (edit.Artist is not null)
                    file.Tag.Performers = [edit.Artist];
                if (edit.Album is not null)
                    file.Tag.Album = edit.Album;
                if (edit.AlbumArtist is not null)
                    file.Tag.AlbumArtists = [edit.AlbumArtist];
                if (edit.Track is { } track)
                    file.Tag.Track = track;
                if (edit.Disc is { } disc)
                    file.Tag.Disc = disc;
                file.Save();
            }

            File.Replace(tempPath, filePath, null);
        }
        finally
        {
            File.Delete(tempPath);
        }

        File.SetLastWriteTimeUtc(filePath, modified);
    }

    /// <summary>
    /// Atomically swaps in the new contents and keeps the original as .bak. An
    /// existing .bak is left as it is, since it is the older of the two copies.
    /// The modified time is kept because the audio hasn't changed.
    /// </summary>
    private static void ReplaceKeepingBackup(string filePath, byte[] contents)
    {
        var modified = File.GetLastWriteTimeUtc(filePath);
        var tempPath = filePath + ".tmp";
        var backupPath = filePath + ".bak";

        try
        {
            File.WriteAllBytes(tempPath, contents);
            File.Replace(tempPath, filePath, File.Exists(backupPath) ? null : backupPath);
        }
        finally
        {
            File.Delete(tempPath);
        }

        File.SetLastWriteTimeUtc(filePath, modified);
    }

    /// <summary>
    /// Reads a single mp3 file's tag data into an Mp3TagRecord.
    /// </summary>
    private static Mp3TagRecord ReadSingleFile(string filePath)
    {
        using var file = TagLib.File.Create(filePath);
        return ToRecord(filePath, file);
    }

    private static Mp3TagRecord ReadFromMemory(string filePath, byte[] contents)
    {
        using var file = TagLib.File.Create(new MemoryFileAbstraction(filePath, contents));
        return ToRecord(filePath, file);
    }

    private static Mp3TagRecord ToRecord(string filePath, TagLib.File file)
    {
        var tag = file.Tag;
        var properties = file.Properties;

        var warnings = new List<string>();
        if (file.PossiblyCorrupt)
            warnings.AddRange(file.CorruptionReasons);

        // TagLib can read the tags of a file whose content isn't MPEG audio at
        // all, such as a WAV renamed to .mp3, without reporting a problem.
        if (properties is null || properties.Duration == TimeSpan.Zero)
            warnings.Add("No MPEG audio detected. The file may not really be an mp3.");

        return CreateFileRecord(filePath) with
        {
            Title = NullIfEmpty(tag.Title),
            Artist = NullIfEmpty(tag.JoinedPerformers),
            Album = NullIfEmpty(tag.Album),
            AlbumArtist = NullIfEmpty(tag.JoinedAlbumArtists),
            Genre = NullIfEmpty(tag.JoinedGenres),
            Composer = NullIfEmpty(tag.JoinedComposers),
            Comment = NullIfEmpty(tag.Comment),
            Year = tag.Year,
            Track = tag.Track,
            TrackCount = tag.TrackCount,
            Disc = tag.Disc,
            Duration = properties?.Duration ?? TimeSpan.Zero,
            BitrateKbps = properties?.AudioBitrate ?? 0,
            SampleRateHz = properties?.AudioSampleRate ?? 0,
            Channels = properties?.AudioChannels ?? 0,
            TagTypes = file.TagTypesOnDisk.ToString(),
            PictureCount = tag.Pictures.Length,
            Status = warnings.Count > 0 ? Mp3Status.Warning : Mp3Status.Ok,
            Errors = warnings
        };
    }

    /// <summary>Creates a record holding only what the file system knows about the file.</summary>
    private static Mp3TagRecord CreateFileRecord(string filePath)
    {
        var info = new FileInfo(filePath);

        return new Mp3TagRecord
        {
            FilePath = filePath,
            FileName = info.Name,
            ContainingDirectoryName = info.Directory!.Name,
            SizeBytes = info.Length,
            Created = info.CreationTime,
            Modified = info.LastWriteTime
        };
    }

    /// <summary>Lets TagLib read file contents held in memory. Its name selects the mp3 parser.</summary>
    private sealed class MemoryFileAbstraction(string name, byte[] contents) : TagLib.File.IFileAbstraction
    {
        public string Name => name;
        public Stream ReadStream => new MemoryStream(contents, writable: false);
        public Stream WriteStream => throw new NotSupportedException();
        public void CloseStream(Stream stream) => stream.Dispose();
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
