using Mp3Tag.Albums;

namespace Mp3TagFromApi.Wpf;

/// <summary>One mp3 as shown in the grid: its tags now, and with an arrow the values Apply would write.</summary>
public sealed class FileRow(AudioFile file, FilePlan? plan, bool updated)
{
    public AudioFile File { get; } = file;

    public FilePlan? Plan { get; } = plan;

    public string FilePath => File.Path;

    private TagChange? Change => Plan?.Change;

    public bool HasChanges => Plan?.HasChanges == true;

    public bool IsProblem => File.Error is not null;

    public string Status =>
        File.Error is not null ? "Error"
        : HasChanges ? "Updatable"
        : updated ? "Updated"
        : "OK";

    /// <summary>Picks the status colour: what needs attention, what is pending, what is done.</summary>
    public string Tone => Status switch
    {
        "Error" => "Critical",
        "Updatable" => "Pending",
        "Updated" => "Success",
        _ => "Neutral"
    };

    public string FileName => Change?.FileName is { } name ? $"→ {name}" : File.FileName;

    public string FileNameTooltip => Change?.FileName is { } name ? $"Now: {File.FileName}\nNew: {name}" : File.FileName;

    public string? Title => Shown(Change?.Title, File.Title);

    public string? Artist => Shown(Change?.Artist, File.Artist);

    public string? Album => Shown(Change?.Album, File.Album);

    /// <summary>A new value with an arrow ("→ (removed)" for an emptied tag), or the file's own.</summary>
    private static string? Shown(string? change, string? current) =>
        change is null ? current : change.Length == 0 ? "→ (removed)" : $"→ {change}";

    /// <summary>The tags as Apply would leave them, for Edit tags.</summary>
    public TagValues Values => new(File.FileName,
        Change?.Title ?? File.Title, Change?.Artist ?? File.Artist, Change?.Album ?? File.Album,
        Change?.AlbumArtist ?? File.AlbumArtist, Change?.Genre ?? File.Genre,
        Change?.Year ?? File.Year, Change?.Track ?? File.Track, Change?.Disc ?? File.Disc);

    /// <summary>The disc and track, such as "1-02", or just the track. A new value starts with an arrow.</summary>
    public string Number
    {
        get
        {
            var (disc, track) = NewDiscAndTrack;
            var text = track == 0 ? "" : disc > 0 && (Change?.DiscCount ?? File.DiscCount) > 1 ? $"{disc}-{track:00}" : track.ToString();
            return Change is { Track: not null } or { Disc: not null } && text.Length > 0 ? $"→ {text}" : text;
        }
    }

    /// <summary>Sorts by disc, then track.</summary>
    public uint NumberOrder => NewDiscAndTrack is var (disc, track) ? disc * 10000 + track : 0;

    private (uint Disc, uint Track) NewDiscAndTrack => (Change?.Disc ?? File.Disc, Change?.Track ?? File.Track);

    public TimeSpan Duration => File.Duration;

    public string Length => File.Error is not null || Duration <= TimeSpan.Zero ? "" : Durations.Format(Duration);

    public string Details => File.Error is { } error ? error : string.Join("; ", Plan?.Changes ?? []);

    public string? DetailsTooltip => File.Error is { } error ? $"Error: {error}"
        : Plan is { HasChanges: true } p ? string.Join(Environment.NewLine, p.Changes) : null;

    /// <summary>True if the file name, a tag, or a value Apply would write contains <paramref name="text"/>.</summary>
    public bool Matches(string text) =>
        new[] { File.FileName, File.Title, File.Artist, File.Album, Change?.Title, Change?.Artist, Change?.Album, Change?.FileName }
            .Any(value => value?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false);
}
