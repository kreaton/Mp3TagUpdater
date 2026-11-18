namespace Mp3Tag.Albums;

/// <summary>How well an album fits a folder of files: the number of tracks, and the total playing time.</summary>
public enum FitLevel
{
    /// <summary>Not checked yet.</summary>
    Unknown,

    /// <summary>The album has a different number of tracks, so it can't be used for the folder.</summary>
    TracksDiffer,

    /// <summary>The same number of tracks, but the total playing time differs: probably another edition.</summary>
    LengthDiffers,

    /// <summary>The same number of tracks; the source lists no length for some tracks, so it can't be compared.</summary>
    TracksMatch,

    /// <summary>The same number of tracks and about the same total playing time.</summary>
    Match,
}

/// <summary>
/// Compares an album with a folder by the number of tracks and their total playing time. Single tracks may
/// differ by a second or two between a rip and the source's listing, so only the total is compared, within
/// <see cref="Tolerance"/>.
/// </summary>
/// <param name="AlbumLength">The album's total, or null when the source lists no length for some track.</param>
public sealed record AlbumFit(int Tracks, int Files, TimeSpan? AlbumLength, TimeSpan FolderLength)
{
    /// <summary>2 % of the folder's playing time, but at least 15 seconds.</summary>
    public TimeSpan Tolerance => TimeSpan.FromSeconds(Math.Max(15, FolderLength.TotalSeconds * 0.02));

    /// <summary>Album minus folder, or null when the album's length isn't known.</summary>
    public TimeSpan? LengthDifference => AlbumLength - FolderLength;

    public FitLevel Level =>
        Tracks != Files ? FitLevel.TracksDiffer
        : LengthDifference is not { } d ? FitLevel.TracksMatch
        : d.Duration() <= Tolerance ? FitLevel.Match
        : FitLevel.LengthDiffers;

    /// <summary>The fit of <paramref name="album"/> for <paramref name="files"/>. Files that can't be read count with no length.</summary>
    public static AlbumFit Of(AlbumInfo album, IReadOnlyList<AudioFile> files) =>
        new(album.Tracks.Count, files.Count, TotalLength(album), PlayingTime(files));

    /// <summary>The fit from a search result's track count alone, before the album is loaded.</summary>
    public static AlbumFit FromTrackCount(int tracks, IReadOnlyList<AudioFile> files) =>
        new(tracks, files.Count, null, PlayingTime(files));

    /// <summary>The sum of the track lengths, or null when the source lists no length for some track.</summary>
    public static TimeSpan? TotalLength(AlbumInfo album) =>
        album.Tracks.Count > 0 && album.Tracks.All(t => t.Duration is not null)
            ? TimeSpan.FromTicks(album.Tracks.Sum(t => t.Duration!.Value.Ticks))
            : null;

    /// <summary>The files' total playing time, leaving out files that can't be read.</summary>
    public static TimeSpan PlayingTime(IReadOnlyList<AudioFile> files) =>
        TimeSpan.FromTicks(files.Where(f => f.Error is null).Sum(f => f.Duration.Ticks));
}
