using System.Text;
using System.Text.RegularExpressions;

namespace Mp3Tag.Albums;

/// <summary>Turns album and track titles into names Windows accepts as file names.</summary>
public static partial class FileNames
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Replaces / \ : | with "-", " with ', and drops ? * &lt; &gt; and control characters. Also trims trailing
    /// dots and spaces, avoids reserved names such as CON, and keeps the result at most <paramref name="maxLength"/> long.
    /// </summary>
    public static string Sanitize(string name, string fallback = "untitled", int maxLength = 150)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            switch (c)
            {
                case '/' or '\\' or ':' or '|': sb.Append('-'); break;
                case '"': sb.Append('\''); break;
                case '?' or '*' or '<' or '>': break;
                default: sb.Append(char.IsControl(c) ? ' ' : c); break;
            }
        }

        var result = Whitespace().Replace(sb.ToString(), " ").Trim().TrimEnd('.', ' ');
        if (result.Length > maxLength) result = result[..maxLength].TrimEnd('.', ' ');
        if (result.Length == 0) return fallback;
        if (Reserved.Contains(result.Split('.')[0].Trim())) result = "_" + result;
        return result;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>Compares strings with numbers by value, so "Track 2" sorts before "Track 10".</summary>
public sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int startX = i, startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                var a = x[startX..i].TrimStart('0');
                var b = y[startY..j].TrimStart('0');
                if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                var byDigits = string.CompareOrdinal(a, b);
                if (byDigits != 0) return byDigits;
            }
            else
            {
                var byChar = string.Compare(x, i, y, j, 1, StringComparison.CurrentCultureIgnoreCase);
                if (byChar != 0) return byChar;
                i++;
                j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
