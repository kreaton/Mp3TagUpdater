using Mp3TagUpdater.Services;

namespace Mp3TagUpdater.Tests;

public class MojibakeTextTests
{
    [Theory]
    [InlineData("Ã¦Ã¸Ã¥ Ã†Ã˜Ã….txt", "æøå ÆØÅ.txt", 1252)]
    [InlineData("SÃ¸ren Ã…rhus", "Søren Århus", 1252)]
    [InlineData("CafÃ© naÃ¯ve", "Café naïve", 1252)]
    [InlineData("Donâ€™t â€“ stop", "Don’t – stop", 1252)]
    [InlineData("25Â°C", "25°C", 1252)]
    [InlineData("Ã\u0098 Ã\u0085", "Ø Å", 1252)]                // read as ISO-8859-1: C1 controls instead of ˜ and …
    [InlineData("ÃƒÂ¸l", "øl", 1252)]                           // misencoded twice
    [InlineData("Ð¿Ñ€Ð¸Ð²ÐµÑ‚", "привет", 1252)]
    [InlineData("ðŸ˜€.png", "😀.png", 1252)]
    [InlineData("├ª├╕├Ñ", "æøå", 437)]
    [InlineData("├ª├©├Ñ", "æøå", 850)]
    [InlineData("S├©ren ├àrhus", "Søren Århus", 850)]
    public void Repairs_mojibake(string text, string expected, int codePage)
    {
        var repair = MojibakeText.TryRepair(text);

        Assert.NotNull(repair);
        Assert.Equal(expected, repair.Fixed);
        Assert.Equal(codePage, repair.CodePage);
        Assert.True(repair.Certain);
    }

    [Theory]
    [InlineData("æøå ÆØÅ")]
    [InlineData("Søren Århus.txt")]
    [InlineData("São Paulo - Ação")]
    [InlineData("CHÂTEAU")]
    [InlineData("naïve café, pâte")]
    [InlineData("Ünïcödé ß")]
    [InlineData("plain ascii (1).txt")]
    [InlineData("привет")]
    [InlineData("")]
    public void Leaves_correct_text_alone(string text)
    {
        Assert.Null(MojibakeText.TryRepair(text));
    }

    [Fact]
    public void Repairs_only_the_wrong_part_of_mixed_text()
    {
        var repair = MojibakeText.TryRepair("ø og Ã¸");

        Assert.NotNull(repair);
        Assert.Equal("ø og ø", repair.Fixed);
    }

    [Fact]
    public void A_single_unusual_sequence_is_uncertain()
    {
        var repair = MojibakeText.TryRepair("MaÅ‚y");

        Assert.NotNull(repair);
        Assert.Equal("Mały", repair.Fixed);
        Assert.False(repair.Certain);
    }

    [Fact]
    public void Two_unusual_sequences_are_certain()
    {
        var repair = MojibakeText.TryRepair("MaÅ‚y Å‚");

        Assert.NotNull(repair);
        Assert.True(repair.Certain);
    }

    [Theory]
    [InlineData("Ã jne", 1)]
    [InlineData("Â text", 1)]
    [InlineData("Donâ€ t", 1)]
    [InlineData("Bj�rn", 1)]
    [InlineData("Øjne", 0)]
    [InlineData("Don’t", 0)]
    public void Reports_damage_that_cannot_be_repaired(string text, int warnings)
    {
        Assert.Equal(warnings, MojibakeText.FindUnrecoverable(text).Count);
    }

    [Fact]
    public void Only_the_given_code_pages_are_tried()
    {
        var only1252 = new[] { CodePageMap.Get(1252) };

        Assert.Null(MojibakeText.TryRepair("├ª├©├Ñ", only1252));
    }

    [Fact]
    public void Rejects_unknown_and_multibyte_code_pages()
    {
        Assert.Throws<ArgumentException>(() => CodePageMap.Get(12345));
        Assert.Throws<ArgumentException>(() => CodePageMap.Get(65001));
    }
}
