using System.Text;
using System.Text.RegularExpressions;

namespace Mp3Tag.Text;

/// <summary>The outcome of <see cref="MisencodedText.Fix"/>.</summary>
/// <param name="Text">The corrected text.</param>
/// <param name="Unresolved">Letters that couldn't be worked out and were left as they were.</param>
public sealed record MisencodedFix(string Text, int Unresolved);

/// <summary>
/// Repairs text whose UTF-8 bytes were read as Windows-1252, which turns each letter
/// such as æ, ø or å into two characters: "Ã¦", "Ã¸", "Ã¥".
/// </summary>
/// <remarks>
/// In the upper-case letters (Æ = C3 86, Ø = C3 98, Å = C3 85, and also Ä, Ö) the second byte
/// is a control code, and some programs have replaced it with a space: "Ã jne" was "Øjne". That
/// letter can only be recovered from another copy of the same words, such as the file name, so
/// <see cref="Fix"/> takes reference texts. When the references are trusted, they also win where
/// they spell a word with a different letter: some rippers wrote file names with one wrong letter
/// throughout ("vÃœre" for "være"), which the title tag gets right.
/// </remarks>
public static partial class MisencodedText
{
    // Windows-1252 characters for the bytes 0x80-0x9F. The bytes it leaves undefined, and every
    // byte from 0xA0 to 0xBF, read as the Latin-1 character with the same number.
    private const string Cp1252High = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008DŽ\u008F\u0090‘’“”•–—˜™š›œ\u009DžŸ";

    /// <summary>
    /// Returns the corrected text, or null when there is nothing to correct. A letter whose byte
    /// was lost is looked up in the <paramref name="references"/>, which may contain the same words
    /// spelled correctly. With <paramref name="trustReferences"/>, the references also replace
    /// letters that decoded cleanly but differently.
    /// </summary>
    public static MisencodedFix? Fix(string? text, IEnumerable<string?> references, bool trustReferences = false)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var tokens = Tokenize(text);
        if (tokens.Count == 0)
            return null;

        var letters = tokens.Select(t => t.Decoded).ToArray();
        ResolveFromReferences(text, tokens, letters, references.OfType<string>().ToList(), trustReferences);

        var result = new StringBuilder(text.Length);
        var unresolved = 0;
        var position = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            result.Append(text, position, token.Start - position);
            if (letters[i] is { } letter)
            {
                result.Append(letter);
            }
            else
            {
                result.Append(text, token.Start, token.Length);
                unresolved++;
            }
            position = token.Start + token.Length;
        }
        result.Append(text, position, text.Length - position);

        var fixedText = result.ToString();
        // Text whose only fault couldn't be worked out is still returned, so the caller can report it.
        return fixedText == text && unresolved == 0 ? null : new MisencodedFix(fixedText, unresolved);
    }

    /// <summary>A misencoded letter: where it starts, how many characters it takes, and the letter it decodes to.</summary>
    private sealed record Token(int Start, int Length, char? Decoded);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();

        for (var i = 0; i < text.Length; i++)
        {
            var lead = text[i];
            if (lead is not ('Ã' or 'Â'))
                continue;

            if (i + 1 < text.Length && ContinuationByte(text[i + 1]) is { } b)
            {
                // Â only ever leads symbols; skip the C1 controls it would decode to.
                if (lead == 'Â' && b < 0xA0)
                    continue;
                var decoded = (char)((lead == 'Ã' ? 0xC0 : 0x80) + (b - 0x80));
                tokens.Add(new Token(i, 2, decoded));
                i++;
            }
            else if (lead == 'Ã' && i + 2 < text.Length && text[i + 1] == ' ' && char.IsLetter(text[i + 2]))
            {
                // The second byte was lost: "Ã jne". The letter is one of À-ß, but which one is unknown.
                tokens.Add(new Token(i, 2, null));
                i++;
            }
        }

        return tokens;
    }

    /// <summary>The byte 0x80-0xBF that <paramref name="c"/> stands for in Windows-1252, if any.</summary>
    private static int? ContinuationByte(char c)
    {
        if (c is >= ' ' and <= '¿' or >= '\u0080' and <= '\u009F')
            return c;
        var index = Cp1252High.IndexOf(c);
        return index >= 0 ? 0x80 + index : null;
    }

    /// <summary>
    /// For each word holding misencoded letters, looks for the same word in the references with
    /// one letter in place of each, and takes those letters when every reference that has the word
    /// agrees.
    /// </summary>
    private static void ResolveFromReferences(string text, List<Token> tokens, char?[] letters, List<string> references, bool trust)
    {
        if (references.Count == 0)
            return;

        foreach (var word in Words(text, tokens))
        {
            if (!trust && word.TokenIndexes.All(i => tokens[i].Decoded is not null))
                continue;

            var pattern = new StringBuilder(@"(?<![\p{L}\p{N}])");
            var position = word.Start;
            foreach (var index in word.TokenIndexes)
            {
                var token = tokens[index];
                pattern.Append(Regex.Escape(text[position..token.Start])).Append(@"(\p{L})");
                position = token.Start + token.Length;
            }
            pattern.Append(Regex.Escape(text[position..word.End])).Append(@"(?![\p{L}\p{N}])");

            var regex = new Regex(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var found = references
                .SelectMany(r => regex.Matches(r))
                .Select(m => m.Groups.Values.Skip(1).Select(g => g.Value[0]).ToArray())
                .Where(chars => chars.All(c => c >= 'À'))
                .Select(chars => new string(chars))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (found.Count != 1)
                continue;

            for (var i = 0; i < word.TokenIndexes.Count; i++)
            {
                var index = word.TokenIndexes[i];
                var letter = found[0][i];
                // A lost byte is always an upper-case letter (C3 80-C3 9F is À-ß).
                if (tokens[index].Decoded is null)
                    letters[index] = char.ToUpperInvariant(letter);
                else if (trust)
                    letters[index] = letter;
            }
        }
    }

    private sealed record Word(int Start, int End, List<int> TokenIndexes);

    /// <summary>Groups the tokens into the words they belong to, with the word's extent in <paramref name="text"/>.</summary>
    private static List<Word> Words(string text, List<Token> tokens)
    {
        var words = new List<Word>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var start = tokens[i].Start;
            while (start > 0 && IsWordChar(text[start - 1]))
                start--;

            var indexes = new List<int> { i };
            var end = tokens[i].Start + tokens[i].Length;
            while (true)
            {
                while (end < text.Length && IsWordChar(text[end]) && !(i + 1 < tokens.Count && tokens[i + 1].Start == end))
                    end++;
                if (i + 1 < tokens.Count && tokens[i + 1].Start == end)
                {
                    i++;
                    indexes.Add(i);
                    end = tokens[i].Start + tokens[i].Length;
                    continue;
                }
                break;
            }

            words.Add(new Word(start, end, indexes));
        }
        return words;

        // Letters and digits, but not the lead characters of a following token.
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) && c is not ('Ã' or 'Â');
    }
}
