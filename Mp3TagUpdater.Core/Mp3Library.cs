using Mp3TagUpdater.Models;

namespace Mp3TagUpdater.Services;

/// <summary>
/// Handles scanning a directory for mp3 files, reading their tags into
/// Mp3TagRecord instances, repairing files that can't be read, and updating
/// the Album tag from the folder name when it is missing.
/// </summary>
/// <param name="onMessage">
/// Receives a line for each proposed or actual change and each failed write.
/// It is called on the thread doing the work.
/// </param>
public class Mp3Library(Action<Mp3Message>? onMessage = null)
{
    /// <summary>
    /// Reads every mp3 in <see cref="Mp3RunOptions.Directory"/>, repairs unreadable
    /// files and fills in missing albums. Repairs come first so that a repaired file
    /// can also get an album. A cancelled run stops before the next file, and the
    /// files it didn't reach keep the record they had at that point.
    /// </summary>
    public Mp3RunResult Run(Mp3RunOptions options, IProgress<Mp3Progress>? progress = null, CancellationToken cancellation = default)
    {
        var records = ReadTags(options.Directory, options.Recursive, progress, cancellation);
        records = RepairUnreadableFiles(records, options.DryRun, progress, cancellation);
        records = UpdateMissingAlbumTags(records, options.DryRun, progress, cancellation);
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
    /// Sets a missing Album tag to the name of the folder containing the file.
    /// Files that are unreadable or flagged with a warning are skipped, so that
    /// tags are never rewritten on a file that may be damaged or not really an mp3.
    /// </summary>
    public List<Mp3TagRecord> UpdateMissingAlbumTags(List<Mp3TagRecord> records, bool dryRun = true,
        IProgress<Mp3Progress>? progress = null, CancellationToken cancellation = default) =>
        Process(records, "Filling in albums", CanUpdateAlbum, r => UpdateAlbum(r, dryRun), progress, cancellation);

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

    private static bool CanUpdateAlbum(Mp3TagRecord record) =>
        record.AlbumWasMissing &&
        record.Status is Mp3Status.Ok or Mp3Status.Repairable or Mp3Status.Repaired;

    private Mp3TagRecord UpdateAlbum(Mp3TagRecord record, bool dryRun)
    {
        var album = record.ContainingDirectoryName;
        IReadOnlyList<string> changes = [.. record.Changes, $"Set Album to \"{album}\""];

        if (dryRun)
        {
            Report(Mp3MessageKind.Change, $"[DRY RUN] Would set Album to \"{album}\": {record.FileName}");
            return record with
            {
                Status = record.Status == Mp3Status.Ok ? Mp3Status.Updatable : record.Status,
                Changes = changes
            };
        }

        try
        {
            WriteAlbumTag(record.FilePath, album);
            Report(Mp3MessageKind.Change, $"Set Album to \"{album}\": {record.FileName}");

            return ReadSingleFile(record.FilePath) with
            {
                Status = record.Status == Mp3Status.Repaired ? Mp3Status.Repaired : Mp3Status.Updated,
                Errors = record.Errors,
                Changes = changes
            };
        }
        catch (Exception ex)
        {
            Report(Mp3MessageKind.Error, $"Failed to set Album on '{record.FilePath}': {ex.Message}");
            return record with
            {
                Status = Mp3Status.Error,
                Errors = [.. record.Errors, $"Album update failed: {ex.Message}"]
            };
        }
    }

    private void Report(Mp3MessageKind kind, string text) => onMessage?.Invoke(new Mp3Message(kind, text));

    /// <summary>
    /// Writes the tag to a temporary copy and then swaps it in, so a failed save
    /// can't damage the original. The modified time is kept because the audio
    /// hasn't changed.
    /// </summary>
    private static void WriteAlbumTag(string filePath, string album)
    {
        var modified = File.GetLastWriteTimeUtc(filePath);
        var tempPath = filePath + ".tmp";

        try
        {
            File.Copy(filePath, tempPath, overwrite: true);

            // The .tmp extension means TagLib has to be told the file type.
            using (var file = TagLib.File.Create(tempPath, "audio/mpeg", TagLib.ReadStyle.Average))
            {
                file.Tag.Album = album;
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
