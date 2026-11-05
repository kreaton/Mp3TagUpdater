using Mp3TagUpdater.Models;

namespace Mp3TagUpdater.Services;

/// <summary>
/// Handles scanning a directory for mp3 files, reading their tags into
/// Mp3TagRecord instances, and updating the Album tag from the folder
/// name when it is missing.
/// </summary>
public class Mp3Library
{
    /// <summary>
    /// Recursively (or top-level only) scans <paramref name="rootDirectory"/> for
    /// .mp3 files and reads their tag data into a list of records. A file that
    /// can't be read still gets a record, with its status set to Error.
    /// </summary>
    public List<Mp3TagRecord> ReadTags(string rootDirectory, bool recursive = true)
    {
        if (!Directory.Exists(rootDirectory))
            throw new DirectoryNotFoundException($"Directory not found: {rootDirectory}");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var mp3Files = Directory.EnumerateFiles(rootDirectory, "*.mp3", searchOption);

        var records = new List<Mp3TagRecord>();

        foreach (var filePath in mp3Files)
        {
            try
            {
                records.Add(ReadSingleFile(filePath));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Failed to read '{filePath}': {ex.Message}");
                Console.ResetColor();
                records.Add(CreateFileRecord(filePath) with
                {
                    Status = Mp3Status.Error,
                    Errors = [ex.Message]
                });
            }
        }

        return records;
    }

    /// <summary>
    /// Attempts to repair every record with Error status. Each repair is first
    /// verified in memory. The file is only changed if <paramref name="dryRun"/>
    /// is false, and the original is then kept alongside it as .bak.
    /// </summary>
    public List<Mp3TagRecord> RepairUnreadableFiles(List<Mp3TagRecord> records, bool dryRun = true) =>
        records.Select(r => r.Status == Mp3Status.Error ? RepairFile(r, dryRun) : r).ToList();

    private static Mp3TagRecord RepairFile(Mp3TagRecord record, bool dryRun)
    {
        try
        {
            var repair = Id3v2Repair.TryRepair(File.ReadAllBytes(record.FilePath));
            if (repair is null)
            {
                WriteColored(ConsoleColor.Red, $"  No automatic repair for '{record.FilePath}'");
                return record;
            }

            // Throws if TagLib still can't read the repaired data.
            var verified = ReadFromMemory(record.FilePath, repair.Data);

            WriteColored(ConsoleColor.Yellow, dryRun
                ? $"  [DRY RUN] Would repair {record.FileName}: {string.Join("; ", repair.Actions)}"
                : $"  Repairing {record.FileName}: {string.Join("; ", repair.Actions)}");

            if (dryRun)
            {
                return verified with
                {
                    Status = Mp3Status.Repairable,
                    Errors = record.Errors,
                    RepairActions = repair.Actions
                };
            }

            ReplaceKeepingBackup(record.FilePath, repair.Data);

            return ReadSingleFile(record.FilePath) with
            {
                Status = Mp3Status.Repaired,
                Errors = record.Errors,
                RepairActions = repair.Actions
            };
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red, $"  Failed to repair '{record.FilePath}': {ex.Message}");
            return record with { Errors = [.. record.Errors, $"Repair failed: {ex.Message}"] };
        }
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

    private static void WriteColored(ConsoleColor color, string message)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>Lets TagLib read file contents held in memory. Its name selects the mp3 parser.</summary>
    private sealed class MemoryFileAbstraction(string name, byte[] contents) : TagLib.File.IFileAbstraction
    {
        public string Name => name;
        public Stream ReadStream => new MemoryStream(contents, writable: false);
        public Stream WriteStream => throw new NotSupportedException();
        public void CloseStream(Stream stream) => stream.Dispose();
    }

    /// <summary>
    /// For every record whose Album tag is missing/empty, sets the Album tag
    /// to the name of the directory that contains the file, saves the change
    /// to disk, and returns an updated list of records reflecting the change.
    /// </summary>
    /// <param name="records">Previously-read records to evaluate and update.</param>
    /// <param name="dryRun">If true, reports what would change without writing to disk.</param>
    public List<Mp3TagRecord> UpdateMissingAlbumTags(List<Mp3TagRecord> records, bool dryRun = false)
    {
        var updated = new List<Mp3TagRecord>(records.Count);

        foreach (var record in records)
        {
            if (!record.AlbumWasMissing)
            {
                updated.Add(record);
                continue;
            }

            var newAlbumName = record.ContainingDirectoryName;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(dryRun
                ? $"  [DRY RUN] Would set Album -> \"{newAlbumName}\" for {record.FileName}"
                : $"  Updating Album -> \"{newAlbumName}\" for {record.FileName}");
            Console.ResetColor();

            if (!dryRun)
            {
                try
                {
                    WriteAlbumTag(record.FilePath, newAlbumName);
                    updated.Add(record.WithAlbum(newAlbumName));
                    continue;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  Failed to update '{record.FilePath}': {ex.Message}");
                    Console.ResetColor();
                }
            }

            updated.Add(record);
        }

        return updated;
    }

    /// <summary>
    /// Opens the file, sets the Album tag, and saves it back to disk.
    /// </summary>
    private static void WriteAlbumTag(string filePath, string albumName)
    {
        using var file = TagLib.File.Create(filePath);
        file.Tag.Album = albumName;
        file.Save();
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
