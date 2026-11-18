namespace Mp3Tag.Core.Models;

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

    /// <summary>Readable, and --apply would change its tags or its name.</summary>
    public const string Updatable = "Updatable";

    /// <summary>Its tags or its name have been changed on disk.</summary>
    public const string Updated = "Updated";
}

/// <summary>What a record describes: an mp3, or another file or folder found when fixing names.</summary>
public enum Mp3EntryKind
{
    Mp3,
    File,
    Folder
}

/// <summary>New values for a file's tags and name. A null value is left as it is.</summary>
public sealed record Mp3TagEdit
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? AlbumArtist { get; init; }
    public uint? Track { get; init; }
    public uint? Disc { get; init; }

    /// <summary>The new file name, extension included.</summary>
    public string? FileName { get; init; }

    /// <summary>Misencoded letters are corrected in a tag or the file name.</summary>
    public bool FixesLetters { get; init; }

    /// <summary>The artist is moved out of the title.</summary>
    public bool SplitsTitle { get; init; }

    /// <summary>A missing Album is filled in from the folder name.</summary>
    public bool FillsAlbum { get; init; }

    /// <summary>An Album that was set is replaced by the folder name.</summary>
    public bool ReplacesAlbum { get; init; }

    /// <summary>Missing tags are filled in from the file name.</summary>
    public bool FillsFromFileName { get; init; }

    /// <summary>True if any tag is to be written.</summary>
    public bool ChangesTags =>
        Title is not null || Artist is not null || Album is not null || AlbumArtist is not null || Track is not null || Disc is not null;
}

/// <summary>
/// Immutable snapshot of the tag data for a single MP3 file at the time it was read.
/// One record is created per file when the directory is scanned.
/// </summary>
public record Mp3TagRecord
{
    /// <summary>
    /// An mp3, unless the FixNames option added the record for another file or
    /// a folder whose name needs fixing. Such records have no tags.
    /// </summary>
    public Mp3EntryKind Kind { get; init; } = Mp3EntryKind.Mp3;

    /// <summary>Full path to the mp3 file (or other file or folder) on disk.</summary>
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

    /// <summary>The tags and name that were written, or that --apply would write.</summary>
    public Mp3TagEdit? Edit { get; init; }

    /// <summary>True if the Album tag was empty/missing when the file was first read.</summary>
    public bool AlbumWasMissing => string.IsNullOrWhiteSpace(Album);

    public override string ToString() =>
        $"{FileName,-40} | Artist: {Artist ?? "(none)",-20} | Album: {Album ?? "(none)",-25} | Title: {Title ?? "(none)"}";
}
