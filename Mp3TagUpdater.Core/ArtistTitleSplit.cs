using System.Text.RegularExpressions;

namespace Mp3TagUpdater.Services;

/// <summary>How titles that hold both the artist and the song, separated by "/", are split.</summary>
public enum TitleSplitMode
{
    /// <summary>Titles are left as they are.</summary>
    Off,

    /// <summary>The order is worked out for each folder from what the titles look like.</summary>
    Auto,

    /// <summary>"Artist / Title" everywhere.</summary>
    ArtistFirst,

    /// <summary>"Title / Artist" everywhere.</summary>
    TitleFirst
}

/// <summary>The two halves of a title such as "George Michael / Careless Whisper", in the order they appear.</summary>
public sealed record TitleHalves(string First, string Second);

/// <summary>
/// Splits titles of the form "Artist / Title" or "Title / Artist" into the artist and the song.
/// Compilations rarely keep one order, so <see cref="ArtistFirstByFolder"/> decides it per folder.
/// </summary>
public static partial class ArtistTitleSplit
{
    /// <summary>
    /// Splits at " / ", or at a bare "/" when the title has no " - " (as in "Razz/Kickflipper").
    /// A bare "/" next to " - " is part of a name: "Kasper Winding/Lars Muhl - Sjæl I Flammer".
    /// Returns null when the title isn't two halves.
    /// </summary>
    public static TitleHalves? Split(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        int at, length;
        if ((at = title.IndexOf(" / ", StringComparison.Ordinal)) >= 0)
            length = 3;
        else if (!title.Contains(" - ", StringComparison.Ordinal) && (at = title.IndexOf('/')) >= 0)
            length = 1;
        else
            return null;

        // "KC & The Sunshine Band/ / That's The Way" has a stray slash to drop as well.
        var first = title[..at].Trim().TrimEnd('/').Trim();
        var second = title[(at + length)..].Trim().TrimStart('/').Trim();

        // A third part means the "/" isn't simply between artist and song.
        if (first.Length == 0 || second.Length == 0 || second.Contains(" / ", StringComparison.Ordinal))
            return null;

        return new TitleHalves(first, second);
    }

    /// <summary>Returns (artist, title) from the halves in the given order.</summary>
    public static (string Artist, string Title) Assign(TitleHalves halves, bool artistFirst) =>
        artistFirst ? (halves.First, halves.Second) : (halves.Second, halves.First);

    /// <summary>
    /// Decides for each folder whether its titles put the artist first. The evidence comes from
    /// every title in the scan: a half that is a known artist (an artist tag, or the first part of
    /// an "Artist - Title" title), a half that recurs across tracks, a Eurovision-style
    /// "Name, Country - 1999", or names joined with "&amp;", "feat." or a comma. Parentheses and a
    /// closing "?" or "!" count towards the song. A tie means artist first, the more common order.
    /// </summary>
    /// <param name="tracks">Each track's folder, current artist tag and title.</param>
    public static Dictionary<string, bool> ArtistFirstByFolder(IEnumerable<(string Folder, string? Artist, string? Title)> tracks)
    {
        var list = tracks.ToList();
        var halves = list.Select(t => (t.Folder, Halves: Split(t.Title))).Where(t => t.Halves is not null).ToList();

        var knownArtists = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var (_, artist, title) in list)
        {
            if (!string.IsNullOrWhiteSpace(artist) && !IsVariousArtists(artist))
                knownArtists.Add(artist.Trim());

            if (title is not null && !title.Contains('/') && title.Split(" - ") is [var name, _])
                knownArtists.Add(name.Trim());
        }

        var occurrences = halves
            .SelectMany(h => new[] { h.Halves!.First, h.Halves.Second })
            .GroupBy(s => s, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.CurrentCultureIgnoreCase);

        int ArtistScore(string half)
        {
            var score = 0;
            if (EurovisionEntry().IsMatch(half))
                score += 3;
            if (knownArtists.Contains(half))
                score += 2;
            if (occurrences[half] >= 2)
                score += 1;
            if (JoinedNames().IsMatch(half))
                score += 1;
            if (SongLike().IsMatch(half))
                score -= 1;
            return score;
        }

        return halves
            .GroupBy(h => h.Folder, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(h => ArtistScore(h.Halves!.First) - ArtistScore(h.Halves.Second)) >= 0,
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True for placeholders such as "Various Artists", "Div. kunstnere" or "Diverse".</summary>
    public static bool IsVariousArtists(string artist) => VariousArtists().IsMatch(artist.Trim());

    [GeneratedRegex(@"^(various( artists?)?|v\.?a\.?|div\.?( kunstnere)?|diverse( kunstnere)?|forskellige( kunstnere)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex VariousArtists();

    [GeneratedRegex(@", [^,]+ - \d{4}$")]
    private static partial Regex EurovisionEntry();

    [GeneratedRegex(@"\b(feat\.?|ft\.|featuring)\s| & | og | and |, ", RegexOptions.IgnoreCase)]
    private static partial Regex JoinedNames();

    [GeneratedRegex(@"\(.*\)|[?!]$")]
    private static partial Regex SongLike();
}
