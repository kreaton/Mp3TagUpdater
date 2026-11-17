using Mp3TagUpdater.Models;

namespace Mp3TagUpdater.Services;

/// <summary>Whether a message describes a change (made or proposed) or a failure.</summary>
public enum Mp3MessageKind
{
    Change,
    Error
}

/// <summary>A line worth showing the user while a run is in progress.</summary>
public sealed record Mp3Message(Mp3MessageKind Kind, string Text);

/// <summary>The stage a run is in, and how far it has got through the files.</summary>
public sealed record Mp3Progress(string Phase, int Processed, int Total);

/// <summary>What to scan, and whether to write the changes.</summary>
public sealed record Mp3RunOptions(string Directory)
{
    /// <summary>When true (the default), no mp3 is changed and changes are only proposed.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>Scan subfolders as well as the top-level directory.</summary>
    public bool Recursive { get; init; } = true;

    /// <summary>
    /// Correct letters stored as UTF-8 but read as Windows-1252 ("Ã¸" for "ø") in the tags and
    /// file names. See <see cref="MisencodedText"/>.
    /// </summary>
    public bool FixLetters { get; init; }

    /// <summary>Move the artist out of titles such as "Artist / Title". See <see cref="ArtistTitleSplit"/>.</summary>
    public TitleSplitMode SplitTitles { get; init; } = TitleSplitMode.Off;
}

/// <summary>Every file's record, and whether the run stopped early.</summary>
public sealed record Mp3RunResult(IReadOnlyList<Mp3TagRecord> Records, bool Cancelled)
{
    /// <summary>Number of files per status, in status order, e.g. "OK 12, Updatable 3".</summary>
    public string Summary =>
        string.Join(", ", Records.GroupBy(r => r.Status).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"));

    /// <summary>True if any file was changed, or a dry run found a change to make.</summary>
    public bool HasChanges => Records.Any(r => r.Changes.Count > 0);
}
