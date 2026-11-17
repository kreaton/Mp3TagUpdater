using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Mp3TagUpdater.Services;

/// <summary>A repaired name.</summary>
/// <param name="Original">The name as it was.</param>
/// <param name="Fixed">The name with every misencoded character sequence decoded.</param>
/// <param name="CodePage">The code page the UTF-8 bytes had wrongly been decoded with.</param>
/// <param name="Sequences">How many misencoded character sequences were decoded.</param>
/// <param name="Certain">
/// False when the only evidence is a single two-character sequence that could also be ordinary
/// text, such as "Å‚" (Polish "ł") or "Ëæ". Such repairs are only applied when asked for.
/// </param>
public sealed record MojibakeRepair(string Original, string Fixed, int CodePage, int Sequences, bool Certain);

/// <summary>
/// Finds and repairs mojibake: text whose UTF-8 bytes were decoded with a single-byte code page,
/// which turns each non-ASCII letter into two to four characters. "æøå" read as Windows-1252
/// becomes "Ã¦Ã¸Ã¥", read as DOS code page 850 it becomes "├ª├©├Ñ".
/// </summary>
/// <remarks>
/// Only character sequences that form valid UTF-8 are decoded, so correctly spelled text such as
/// "Århus", "São Paulo" or "CHÂTEAU" is left alone, and text that mixes correct letters with
/// mojibake is repaired where it is wrong. Text that was misencoded twice ("ÃƒÂ¸") is repaired in
/// up to <see cref="MaxPasses"/> passes.
/// </remarks>
public static partial class MojibakeText
{
    /// <summary>The most times text is decoded, for text that was misencoded more than once.</summary>
    public const int MaxPasses = 3;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Repairs <paramref name="text"/> with the default code pages. Returns null when there is nothing to repair.</summary>
    public static MojibakeRepair? TryRepair(string text) =>
        TryRepair(text, CodePageMap.DefaultCodePages.Select(CodePageMap.Get).ToList());

    /// <summary>
    /// Repairs <paramref name="text"/>, trying each code page and keeping the one that decodes the
    /// most sequences (the first one on a tie). Returns null when there is nothing to repair.
    /// </summary>
    public static MojibakeRepair? TryRepair(string text, IReadOnlyList<CodePageMap> codePages)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        MojibakeRepair? best = null;
        foreach (var codePage in codePages)
        {
            var repair = TryRepair(text, codePage);
            if (repair is not null && (best is null || repair.Sequences > best.Sequences))
                best = repair;
        }
        return best;
    }

    private static MojibakeRepair? TryRepair(string text, CodePageMap codePage)
    {
        var current = text;
        var sequences = 0;
        var certain = false;

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var (decoded, count, anyCertain) = DecodePass(current, codePage);
            if (count == 0)
                break;
            current = decoded;
            sequences += count;
            certain |= anyCertain;
        }

        if (sequences == 0)
            return null;

        // Two or more sequences in one name are very unlikely to be ordinary text.
        return new MojibakeRepair(text, current, codePage.CodePage, sequences, certain || sequences >= 2);
    }

    /// <summary>Decodes every sequence of characters whose bytes in <paramref name="codePage"/> form one UTF-8 character.</summary>
    private static (string Text, int Count, bool AnyCertain) DecodePass(string text, CodePageMap codePage)
    {
        var result = new StringBuilder(text.Length);
        var count = 0;
        var anyCertain = false;

        for (var i = 0; i < text.Length;)
        {
            if (TryDecodeAt(text, i, codePage, out var decoded, out var length, out var lead))
            {
                result.Append(decoded);
                count++;
                // Three- and four-byte sequences, and the leads C2/C3 that cover the Latin-1 letters
                // ("Ã¸", "Â©", "├ª"), are the common cases and rarely ordinary text.
                anyCertain |= length >= 3 || lead is 0xC2 or 0xC3;
                i += length;
            }
            else
            {
                result.Append(text[i]);
                i++;
            }
        }

        return (result.ToString(), count, anyCertain);
    }

    private static bool TryDecodeAt(string text, int index, CodePageMap codePage, out string decoded, out int length, out byte lead)
    {
        decoded = "";
        length = 0;

        if (!codePage.TryGetByte(text[index], out lead))
            return false;

        length = lead switch
        {
            >= 0xC2 and <= 0xDF => 2,
            >= 0xE0 and <= 0xEF => 3,
            >= 0xF0 and <= 0xF4 => 4,
            _ => 0
        };
        if (length == 0 || index + length > text.Length)
            return false;

        var bytes = new byte[length];
        bytes[0] = lead;
        for (var k = 1; k < length; k++)
        {
            if (!codePage.TryGetByte(text[index + k], out var b) || b is < 0x80 or > 0xBF)
                return false;
            bytes[k] = b;
        }

        try
        {
            // Rejects overlong forms, surrogates and code points past U+10FFFF.
            decoded = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return IsPlausible(decoded);
    }

    /// <summary>Control characters, private-use and unassigned code points are not what a name held.</summary>
    private static bool IsPlausible(string decoded)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(decoded, 0);
        return category is not (UnicodeCategory.Control or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned);
    }

    /// <summary>
    /// Describes damage in <paramref name="text"/> that can't be repaired automatically, because
    /// a byte or a letter was lost. Run this on the repaired text, if there is one.
    /// </summary>
    public static IReadOnlyList<string> FindUnrecoverable(string text)
    {
        var warnings = new List<string>();

        if (text.Contains('�'))
            warnings.Add("contains the replacement character �; the original letter is lost");

        // The second byte of À-ß (C3 80-9F) and of à (C3 A0, a no-break space in Windows-1252) is
        // often dropped or turned into a space: "Ã jne" was "Øjne", "Ã la" was "à la".
        if (LostAfterATilde().IsMatch(text))
            warnings.Add("\"Ã\" followed by a space is probably a letter whose second byte was lost (such as Ø, Å, Æ or à)");

        if (LostAfterACircumflex().IsMatch(text))
            warnings.Add("\"Â\" followed by a space is probably a no-break space whose second byte was lost");

        if (LostPunctuation().IsMatch(text))
            warnings.Add("\"â€\" not followed by a third character is probably a quote or dash whose last byte was lost");

        return warnings;
    }

    [GeneratedRegex(@"Ã [\p{L}\p{N}]")]
    private static partial Regex LostAfterATilde();

    [GeneratedRegex(@"Â ")]
    private static partial Regex LostAfterACircumflex();

    [GeneratedRegex(@"â€(?![€‚ƒ„…†‡ˆ‰Š‹ŒŽ‘’“”•–—˜™š›œžŸ\u0080-¿])")]
    private static partial Regex LostPunctuation();
}
