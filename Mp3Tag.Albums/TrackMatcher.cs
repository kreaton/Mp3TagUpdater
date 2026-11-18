using System.Text;
using System.Text.RegularExpressions;
using Mp3Tag.Text;

namespace Mp3Tag.Albums;

public enum MatchMethod
{
    /// <summary>Every file was identified by its title tag or file name.</summary>
    TrackName,

    /// <summary>Names didn't identify every file, so files sorted by name were given tracks 1–N.</summary>
    FileOrder,
}

/// <param name="NameMatches">How many files the names identified (all of them when <see cref="MatchMethod.TrackName"/>).</param>
/// <param name="Pairs">One pair per album track, in album order.</param>
public sealed record TrackMatch(MatchMethod Method, int NameMatches, IReadOnlyList<(AudioFile File, AlbumTrack Track)> Pairs);

/// <summary>
/// Decides which file is which track. First by name: a file matches a track when its title tag or its
/// file name (without the leading number) equals the track title, or the file name ends with it
/// ("05 - Monique - Min Kat Den Danser Tango"). Case, accents, punctuation, garbled letters ("SÃ¸ren")
/// and suffixes such as "- 2019 Remaster" are ignored. If that doesn't identify every file exactly once,
/// the files are sorted by name ("Track 2" before "Track 10") and given tracks 1–N in order.
/// </summary>
public static partial class TrackMatcher
{
    public static TrackMatch Match(IReadOnlyList<AudioFile> files, IReadOnlyList<AlbumTrack> tracks)
    {
        if (files.Count != tracks.Count)
            throw new ArgumentException($"{files.Count} files but {tracks.Count} tracks.");

        var byName = MatchByName(files, tracks); // file index → track index
        if (byName.Count == files.Count)
        {
            var fileOfTrack = byName.ToDictionary(p => p.Value, p => p.Key);
            return new(MatchMethod.TrackName, byName.Count,
                tracks.Select((t, i) => (files[fileOfTrack[i]], t)).ToList());
        }

        var inOrder = files.OrderBy(f => f.FileName, NaturalComparer.Instance).ToList();
        return new(MatchMethod.FileOrder, byName.Count, inOrder.Zip(tracks, (f, t) => (f, t)).ToList());
    }

    /// <summary>Returns file index → track index for each file whose name points at exactly one track.</summary>
    internal static Dictionary<int, int> MatchByName(IReadOnlyList<AudioFile> files, IReadOnlyList<AlbumTrack> tracks)
    {
        var fileKeys = files.Select(FileKeys).ToList();
        var trackKeys = tracks.Select(t => TitleKeys(t.Title)).ToList();
        var result = new Dictionary<int, int>();
        var freeTracks = Enumerable.Range(0, tracks.Count).ToHashSet();

        // Pass 1: exact matches. Pass 2: the file name ends with the title ("artist - title").
        foreach (var exact in new[] { true, false })
        {
            var claims = new Dictionary<int, List<int>>(); // track index → files pointing at it
            for (var f = 0; f < files.Count; f++)
            {
                if (result.ContainsKey(f)) continue;
                var candidates = freeTracks
                    .Where(t => trackKeys[t].Any(tk => fileKeys[f].Any(fk =>
                        exact ? fk == tk : tk.Length >= 3 && fk.EndsWith(tk, StringComparison.Ordinal))))
                    .ToList();
                if (candidates.Count != 1) continue;
                if (!claims.TryGetValue(candidates[0], out var claimants)) claims[candidates[0]] = claimants = [];
                claimants.Add(f);
            }

            // A track two files point at stays unresolved.
            foreach (var (track, claimants) in claims)
            {
                if (claimants.Count != 1) continue;
                result[claimants[0]] = track;
                freeTracks.Remove(track);
            }
        }
        return result;
    }

    private static List<string> FileKeys(AudioFile file)
    {
        var keys = new List<string>();
        var stem = LeadingNumber().Replace(Path.GetFileNameWithoutExtension(file.Path), "");

        // Garbled letters ("SÃ¸ren") would never match the source's "Søren", so try the repaired text too.
        foreach (var text in new[] { file.Title, TextRepair.Fix(file.Title, stem), stem, TextRepair.Fix(stem, file.Title) })
            if (text is not null && !IsGeneric(text))
                keys.AddRange(TitleKeys(text));
        return keys.Distinct().ToList();
    }

    private static List<string> TitleKeys(string title) =>
        new[] { Normalize(title), Normalize(Simplify(title)) }.Where(k => k.Length > 0).Distinct().ToList();

    /// <summary>"Track 1", "Track01", "01" or empty: names that say nothing about the song.</summary>
    internal static bool IsGeneric(string text) => GenericName().IsMatch(text);

    /// <summary>Lower-case letters and digits only, with accents removed: "Go'Màren" → "gomaren".</summary>
    internal static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormKD))
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>Drops version notes: "Mexico - 2019 Remaster" → "Mexico", "Fool (Live)" → "Fool".</summary>
    internal static string Simplify(string title) => VersionGroup().Replace(VersionSuffix().Replace(title, ""), "").Trim();

    [GeneratedRegex(@"^\s*(?:track|spor|titel|title|audio)?[\s._-]*\d*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericName();

    // "01 ", "01 - ", "01. ", "1-01 " at the start of a file name.
    [GeneratedRegex(@"^\s*(?:\d+[-.])?\d+\s*[-._)\]]*\s*")]
    private static partial Regex LeadingNumber();

    private const string VersionWords = @"remaster(?:ed)?|version|mix|live|edit|mono|stereo|demo|bonus";

    [GeneratedRegex(@"\s+-\s+[^-]*\b(?:" + VersionWords + @")\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffix();

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*\b(?:" + VersionWords + @")\b[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex VersionGroup();
}
