namespace Mp3TagUpdater.Models;

/// <summary>Outcome of processing a file, as reported in the console and the CSV log.</summary>
public static class Mp3Status
{
    /// <summary>Read without problems.</summary>
    public const string Ok = "OK";

    /// <summary>Readable, but possibly corrupt or containing no MPEG audio.</summary>
    public const string Warning = "Warning";

    /// <summary>Unreadable, and no automatic repair is available.</summary>
    public const string Error = "Error";

    /// <summary>Unreadable on disk, but --apply can repair it. Tags come from a verified in-memory repair.</summary>
    public const string Repairable = "Repairable";

    /// <summary>Was unreadable, and has been repaired on disk.</summary>
    public const string Repaired = "Repaired";

    /// <summary>Readable and missing its Album tag, which --apply would fill in.</summary>
    public const string Updatable = "Updatable";

    /// <summary>Was missing its Album tag, which has been filled in on disk.</summary>
    public const string Updated = "Updated";
}

/// <summary>
/// Immutable snapshot of the tag data for a single MP3 file at the time it was read.
/// One record is created per file when the directory is scanned.
/// </summary>
public record Mp3TagRecord
{
    /// <summary>Full path to the mp3 file on disk.</summary>
    public required string FilePath { get; init; }

    /// <summary>File name only (e.g. "01 - Track.mp3").</summary>
    public required string FileName { get; init; }

    /// <summary>Name of the directory that directly contains the file.</summary>
    public required string ContainingDirectoryName { get; init; }

    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? AlbumArtist { get; init; }
    public string? Genre { get; init; }
    public uint Year { get; init; }
    public uint Track { get; init; }
    public TimeSpan Duration { get; init; }

    public string? Composer { get; init; }
    public string? Comment { get; init; }
    public uint TrackCount { get; init; }
    public uint Disc { get; init; }
    public int BitrateKbps { get; init; }
    public int SampleRateHz { get; init; }
    public int Channels { get; init; }
    public string? TagTypes { get; init; }
    public int PictureCount { get; init; }

    public long SizeBytes { get; init; }
    public DateTime Created { get; init; }
    public DateTime Modified { get; init; }

    /// <summary>One of the <see cref="Mp3Status"/> values.</summary>
    public string Status { get; init; } = Mp3Status.Ok;

    /// <summary>Errors and warnings encountered while reading the file.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Changes made to the file, or that --apply would make.</summary>
    public IReadOnlyList<string> Changes { get; init; } = [];

    /// <summary>True if the Album tag was empty/missing when the file was first read.</summary>
    public bool AlbumWasMissing => string.IsNullOrWhiteSpace(Album);

    /// <summary>
    /// Returns a copy of this record with the Album value updated.
    /// Used after the tag has been written back to the file, so the
    /// in-memory list stays consistent with what's on disk.
    /// </summary>
    public Mp3TagRecord WithAlbum(string album) => this with { Album = album };

    public override string ToString() =>
        $"{FileName,-40} | Artist: {Artist ?? "(none)",-20} | Album: {Album ?? "(none)",-25} | Title: {Title ?? "(none)"}";
}
