using System.Text;

namespace Mp3Tag.Text;

/// <summary>
/// Maps each non-ASCII character of a single-byte code page back to the byte it was decoded from,
/// so text that was wrongly decoded with that code page can be turned back into its original bytes.
/// </summary>
public sealed class CodePageMap
{
    /// <summary>The code pages tried when none are given: Windows Western, then DOS US and DOS Western.</summary>
    public static readonly IReadOnlyList<int> DefaultCodePages = [1252, 437, 850];

    private static readonly Lock CacheLock = new();
    private static readonly Dictionary<int, CodePageMap> Cache = [];

    private readonly Dictionary<char, byte> _bytes;

    private CodePageMap(int codePage, string name, Dictionary<char, byte> bytes)
    {
        CodePage = codePage;
        Name = name;
        _bytes = bytes;
    }

    public int CodePage { get; }

    public string Name { get; }

    /// <summary>The byte 0x80-0xFF that <paramref name="c"/> stands for in this code page, if any.</summary>
    public bool TryGetByte(char c, out byte value) => _bytes.TryGetValue(c, out value);

    /// <summary>Returns the map for a single-byte code page such as 1252, 437 or 850.</summary>
    /// <exception cref="ArgumentException">The code page is unknown or not a single-byte code page.</exception>
    public static CodePageMap Get(int codePage)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(codePage, out var cached))
                return cached;

            var map = Create(codePage);
            Cache[codePage] = map;
            return map;
        }
    }

    private static CodePageMap Create(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Encoding encoding;
        try
        {
            encoding = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            throw new ArgumentException($"Unknown code page: {codePage}", nameof(codePage), e);
        }

        if (!encoding.IsSingleByte)
            throw new ArgumentException($"Code page {codePage} ({encoding.WebName}) is not a single-byte code page.", nameof(codePage));

        var bytes = new Dictionary<char, byte>();
        for (var b = 0x80; b <= 0xFF; b++)
        {
            try
            {
                var text = encoding.GetString([(byte)b]);
                if (text.Length == 1 && text[0] >= '\u0080')
                    bytes.TryAdd(text[0], (byte)b);
            }
            catch (DecoderFallbackException)
            {
                // The byte is undefined in this code page.
            }
        }

        // Text decoded as ISO-8859-1 instead of Windows-1252 holds the C1 controls U+0080-U+009F
        // where Windows-1252 has € ‚ ƒ … and so on. Both stand for the same bytes.
        if (codePage == 1252)
        {
            for (var b = 0x80; b <= 0x9F; b++)
                bytes.TryAdd((char)b, (byte)b);
        }

        return new CodePageMap(codePage, encoding.WebName, bytes);
    }
}
