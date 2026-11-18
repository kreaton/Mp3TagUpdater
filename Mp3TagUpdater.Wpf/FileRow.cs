using System.IO;
using Mp3Tag.Core.Models;

namespace Mp3TagUpdater.Wpf;

/// <summary>One mp3 as shown in the grid.</summary>
public sealed class FileRow(Mp3TagRecord record, string rootDirectory)
{
    public Mp3TagRecord Record { get; } = record;

    public string FilePath => Record.FilePath;

    public string Status => Record.Status;

    /// <summary>Picks the status colour: what needs attention, what is pending, what is done.</summary>
    public string Tone => Record.Status switch
    {
        Mp3Status.Error => "Critical",
        Mp3Status.Warning => "Caution",
        Mp3Status.Repairable or Mp3Status.Updatable => "Pending",
        Mp3Status.Repaired or Mp3Status.Updated => "Success",
        _ => "Neutral"
    };

    /// <summary>The path below the scanned folder. A folder ends in a backslash, so it reads as one.</summary>
    public string RelativePath { get; } = Path.GetRelativePath(rootDirectory, record.FilePath)
        + (record.Kind == Mp3EntryKind.Folder ? Path.DirectorySeparatorChar.ToString() : "");

    public bool HasChanges => Record.Changes.Count > 0;

    /// <summary>The tags and name the next Apply would write, so the grid can show the new values.</summary>
    public Mp3TagEdit? Edit => Record.Status is Mp3Status.Updatable or Mp3Status.Repairable ? Record.Edit : null;

    public string? Album => Edit?.Album is { } album ? $"→ {album}" : Record.Album;

    public string? Artist => Edit?.Artist is { } artist ? $"→ {artist}" : Record.Artist;

    public string? Title => Edit?.Title is { } title ? $"→ {title}" : Record.Title;

    /// <summary>The disc and track, such as "1-02", or just the track. A new value starts with an arrow.</summary>
    public string Number
    {
        get
        {
            var (disc, track) = NewDiscAndTrack;
            var text = track == 0 ? "" : disc > 0 ? $"{disc}-{track:00}" : track.ToString();
            return Edit is { Track: not null } or { Disc: not null } && text.Length > 0 ? $"→ {text}" : text;
        }
    }

    /// <summary>Sorts by disc, then track.</summary>
    public uint NumberOrder => NewDiscAndTrack is var (disc, track) ? disc * 10000 + track : 0;

    private (uint Disc, uint Track) NewDiscAndTrack => (Edit?.Disc ?? Record.Disc, Edit?.Track ?? Record.Track);

    public TimeSpan Duration => Record.Duration;

    public string Length => Duration <= TimeSpan.Zero ? ""
        : Duration.TotalHours >= 1 ? Duration.ToString(@"h\:mm\:ss") : Duration.ToString(@"m\:ss");

    public string Details => string.Join("; ", Record.Changes.Concat(Record.Errors));

    public string? DetailsTooltip
    {
        get
        {
            var lines = Record.Changes.Select(c => $"Change: {c}").Concat(Record.Errors.Select(e => $"Error: {e}")).ToList();
            return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
        }
    }

    public bool IsProblem => Record.Status is Mp3Status.Error or Mp3Status.Warning;

    /// <summary>True if the path, a tag, or a value the next Apply would write contains <paramref name="text"/>.</summary>
    public bool Matches(string text) =>
        new[] { RelativePath, Record.Title, Record.Artist, Record.Album, Edit?.Title, Edit?.Artist, Edit?.Album, Edit?.FileName }
            .Any(value => value?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false);
}
