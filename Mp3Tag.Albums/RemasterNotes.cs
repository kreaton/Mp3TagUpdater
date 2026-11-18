using System.Text.RegularExpressions;

namespace Mp3Tag.Albums;

/// <summary>
/// Removes notes that only say a recording was remastered, which streaming services often add to every title
/// ("Mexico - 2019 Remaster", "Gorilla (2019 Remaster)"). Notes that describe a different recording, such as
/// "(Live)", "- Single Version" or "(Mono)", are kept.
/// </summary>
public static partial class RemasterNotes
{
    public static string Remove(string title)
    {
        var result = Group().Replace(title, "");
        result = Suffix().Replace(result, "");
        result = result.Trim();
        return result.Length > 0 ? result : title; // never leave an empty title
    }

    // "(2019 Remaster)", "[Remastered 2009]", "(Remastered Version)", "(2015 Remastered Edition)"
    [GeneratedRegex(@"\s*[\(\[][^\)\]]*\bremaster(?:ed)?\b[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Group();

    // " - 2019 Remaster", " - Remastered 2011", " - 2011 Remastered Version", " - Remastered"
    [GeneratedRegex(@"\s+-\s+[^-]*\bremaster(?:ed)?\b[^-]*$", RegexOptions.IgnoreCase)]
    private static partial Regex Suffix();
}
