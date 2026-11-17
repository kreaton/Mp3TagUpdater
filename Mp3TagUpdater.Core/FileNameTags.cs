using System.Text.RegularExpressions;

namespace Mp3TagUpdater.Services;

/// <summary>What a file name says about the track. A null or zero value means the name doesn't say.</summary>
public sealed record FileNameInfo(uint Disc, uint Track, string? Artist, string? Title);

/// <summary>
/// Reads the disc, track number, artist and title from file names such as
/// "1-02 Artist - Title.mp3", "02 Artist - Title.mp3", "02. Title.mp3" or "Artist - Title.mp3".
/// </summary>
public static partial class FileNameTags
{
    /// <summary>
    /// Parses the name without its extension. A leading "D-TT" is disc and track, and a leading
    /// number of one to three digits is the track. What follows is "Artist - Title", split at the
    /// first " - ", or just the title when there is no " - ". Returns null when nothing is found.
    /// </summary>
    /// <param name="numberedFolder">
    /// Whether the other files in the folder start with a number too (see <see cref="StartsWithNumber"/>).
    /// A number followed only by a space, without a leading zero, is then a track number; otherwise it
    /// is taken as part of the name, as in "50 Cent - In da Club". "07 ", "7. " and "7 - " are always
    /// track numbers.
    /// </param>
    public static FileNameInfo? Parse(string? fileNameWithoutExtension, bool numberedFolder = true)
    {
        var name = fileNameWithoutExtension?.Trim();
        if (string.IsNullOrEmpty(name))
            return null;

        uint disc = 0, track = 0;
        if (DiscAndTrack().Match(name) is { Success: true } both)
        {
            disc = uint.Parse(both.Groups["disc"].Value);
            track = uint.Parse(both.Groups["track"].Value);
            name = name[both.Length..];
        }
        else if (TrackOnly().Match(name) is { Success: true } number && (numberedFolder || IsClearlyTrack(number)))
        {
            track = uint.Parse(number.Groups["track"].Value);
            name = name[number.Length..];
        }

        name = name.Trim();
        string? artist = null, title = name;
        var at = name.IndexOf(" - ", StringComparison.Ordinal);
        if (at > 0)
        {
            artist = name[..at].Trim();
            title = name[(at + 3)..].Trim();
        }

        artist = NullIfEmpty(artist);
        title = NullIfEmpty(title);
        if (title is null && artist is null && track == 0)
            return null;

        return new FileNameInfo(disc, track, artist, title);

        static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        // A leading zero or a "." or "-" after the number.
        static bool IsClearlyTrack(Match m) =>
            m.Groups["track"].Value is ['0', _, ..] || m.Groups["separator"].Value.Trim().Length > 0;
    }

    /// <summary>True if the name starts with a track number, such as "02 ", "2. " or "1-02 ".</summary>
    public static bool StartsWithNumber(string? fileNameWithoutExtension) =>
        fileNameWithoutExtension is not null &&
        (DiscAndTrack().IsMatch(fileNameWithoutExtension) || TrackOnly().IsMatch(fileNameWithoutExtension));

    // "1-02 ", "1-02 - ", "1-02. ": a disc of one or two digits, a track of one to three.
    [GeneratedRegex(@"^(?<disc>\d{1,2})-(?<track>\d{1,3})(\s*[-.]\s*|\s+)")]
    private static partial Regex DiscAndTrack();

    // "02 ", "02 - ", "02. ", "02-". Four digits would more likely be a year, so they are left alone.
    [GeneratedRegex(@"^(?<track>\d{1,3})(?<separator>\s*[-.]\s*|\s+)")]
    private static partial Regex TrackOnly();
}
