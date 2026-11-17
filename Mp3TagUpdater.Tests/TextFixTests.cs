using Mp3TagUpdater.Services;

namespace Mp3TagUpdater.Tests;

public sealed class MisencodedTextTests
{
    [Theory]
    [InlineData("TÃ¸sedrengene", "Tøsedrengene")]
    [InlineData("KÃ¦rligheden Kalder", "Kærligheden Kalder")]
    [InlineData("Dagen Efter Dagen DerpÃ¥", "Dagen Efter Dagen Derpå")]
    [InlineData("FÃ¶rÃ¤lskad I KÃ¶penhamn", "Förälskad I Köpenhamn")]
    [InlineData("Ne partez pas sans moi / CÃ©line Dion", "Ne partez pas sans moi / Céline Dion")]
    [InlineData("Non ho l'etÃ ", "Non ho l'età")]
    [InlineData("ElseÂ´s Guy", "Else´s Guy")]
    // Upper case: the second byte is a control code (Ø = C3 98, Å = C3 85, Æ = C3 86).
    [InlineData("Mine Ã\u0098jne", "Mine Øjne")]
    [InlineData("40 Ã\u0085r", "40 År")]
    [InlineData("Ã\u0086ble", "Æble")]
    // Read as Windows-1252, the control codes become other characters (0x9C = œ).
    [InlineData("vÃœre", "vÜre")]
    public void Corrects_utf8_read_as_windows_1252(string text, string expected) =>
        Assert.Equal(new MisencodedFix(expected, 0), MisencodedText.Fix(text, []));

    [Theory]
    [InlineData("Tøsedrengene")]
    [InlineData("SÃO PAULO")]
    [InlineData("Plain text")]
    [InlineData("")]
    public void Leaves_correct_text_alone(string text) => Assert.Null(MisencodedText.Fix(text, []));

    [Fact]
    public void Lost_letter_is_taken_from_the_file_name()
    {
        var fix = MisencodedText.Fix("Lis SÃ¸rensen - Mine Ã jne De Skal Se", ["18 Lis Sørensen - Mine Øjne De Skal Se"]);

        Assert.Equal(new MisencodedFix("Lis Sørensen - Mine Øjne De Skal Se", 0), fix);
    }

    [Fact]
    public void Lost_letter_is_upper_case_even_if_the_reference_is_not()
    {
        var fix = MisencodedText.Fix("Du Ã r Den Ende", ["02 lill lindfors - du är den ende"]);

        Assert.Equal("Du Är Den Ende", fix?.Text);
    }

    [Fact]
    public void Lost_letter_without_a_reference_is_left_and_counted()
    {
        var fix = MisencodedText.Fix("SÃ¥n't Ã r Livet", ["something else"]);

        Assert.Equal(new MisencodedFix("Sån't Ã r Livet", 1), fix);
    }

    [Fact]
    public void Trusted_reference_replaces_a_wrong_letter()
    {
        var fix = MisencodedText.Fix("06 VÃœr vor gÃœst", ["Vær vor gæst - fra Skønheden og Udyret"], trustReferences: true);

        Assert.Equal("06 Vær vor gæst", fix?.Text);
    }

    [Fact]
    public void Untrusted_reference_does_not_replace_a_clean_letter()
    {
        var fix = MisencodedText.Fix("VÃœr", ["Vær"]);

        Assert.Equal("VÜr", fix?.Text);
    }

    [Fact]
    public void Disagreeing_references_leave_the_lost_letter()
    {
        var fix = MisencodedText.Fix("Ã r", ["Är", "Ör"]);

        Assert.Equal(1, fix?.Unresolved);
    }
}

public sealed class ArtistTitleSplitTests
{
    [Theory]
    [InlineData("George Michael / Careless Whisper", "George Michael", "Careless Whisper")]
    [InlineData("Razz/Kickflipper", "Razz", "Kickflipper")]
    [InlineData("KC & The Sunshine Band/ / That's The Way (I Like It)", "KC & The Sunshine Band", "That's The Way (I Like It)")]
    [InlineData("Kasper Winding/C.V. Jørgensen / Mig & Charly", "Kasper Winding/C.V. Jørgensen", "Mig & Charly")]
    public void Splits_at_the_slash(string title, string first, string second) =>
        Assert.Equal(new TitleHalves(first, second), ArtistTitleSplit.Split(title));

    [Theory]
    [InlineData("Kasper Winding/Lars Muhl - Sjæl I Flammer")]
    [InlineData("Abba - Dancing Queen")]
    [InlineData("A / B / C")]
    [InlineData(" / Only one half")]
    [InlineData(null)]
    public void Leaves_other_titles_alone(string? title) => Assert.Null(ArtistTitleSplit.Split(title));

    [Fact]
    public void Order_is_decided_per_folder()
    {
        var order = ArtistTitleSplit.ArtistFirstByFolder(
        [
            // A recurring artist, and a Eurovision-style entry, are on the right.
            ("Danish", "Various", "Dagen Efter Dagen Derpå / Thomas Helmig"),
            ("Danish", "Various", "Kom Nu Hjem / Thomas Helmig"),
            ("Danish", "Various", "Take Me to Your Heaven / Charlotte Nilsson, Sverige - 1999"),
            // An artist known from an "Artist - Title" title, and a song in parentheses.
            ("Pop", "Various", "Michael Bolton - Said I Loved You"),
            ("Pop", "Various", "Michael Bolton / How Am I Supposed To Live Without You"),
            ("Pop", "Various", "Jojo / Leave (Get Out)")
        ]);

        Assert.False(order["Danish"]);
        Assert.True(order["Pop"]);
    }

    [Fact]
    public void A_tie_means_artist_first()
    {
        var order = ArtistTitleSplit.ArtistFirstByFolder([("Folder", null, "One / Two")]);

        Assert.True(order["Folder"]);
    }
}
