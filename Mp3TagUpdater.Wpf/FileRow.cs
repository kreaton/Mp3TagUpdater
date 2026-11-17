using System.IO;
using Mp3TagUpdater.Models;

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

    public string RelativePath { get; } = Path.GetRelativePath(rootDirectory, record.FilePath);

    public bool HasChanges => Record.Changes.Count > 0;

    /// <summary>True when an album will be filled in, so the grid can show the album it will get.</summary>
    public bool AlbumPending => Record.AlbumWasMissing && HasChanges && Record.Status is Mp3Status.Updatable or Mp3Status.Repairable;

    public string? Album => AlbumPending ? $"→ {Record.ContainingDirectoryName}" : Record.Album;

    public string? Artist => Record.Artist;

    public string? Title => Record.Title;

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

    public bool Matches(string text) =>
        RelativePath.Contains(text, StringComparison.CurrentCultureIgnoreCase) ||
        (Title?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
        (Artist?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
        (Record.Album?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false);
}
