namespace Mp3Tag.Text;

/// <summary>
/// One call for "fix the garbled letters in this text if it is safe to": combines
/// <see cref="MisencodedText"/> (which can recover a lost letter from a reference such as the file name)
/// with <see cref="MojibakeText"/> (which also knows the DOS code pages and text garbled twice).
/// </summary>
public static class TextRepair
{
    /// <summary>
    /// The text with misencoded letters corrected, or null when there is nothing to correct or the
    /// correction isn't certain. <paramref name="references"/> are other texts that may spell the same
    /// words correctly, such as the file name for a title tag.
    /// </summary>
    public static string? Fix(string? text, params IEnumerable<string?> references)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var current = text;
        if (MisencodedText.Fix(current, references) is { Unresolved: 0 } fix)
            current = fix.Text;
        if (MojibakeText.TryRepair(current) is { Certain: true } repair)
            current = repair.Fixed;

        return current == text ? null : current;
    }

    /// <summary>The text with misencoded letters corrected, or the text as it is.</summary>
    public static string FixOrKeep(string text, params IEnumerable<string?> references) => Fix(text, references) ?? text;
}
