using Mp3Tag.Albums.Lookup;
using Mp3Tag.Text;

namespace Mp3Tag.Tests.Albums;

public class LookupTests
{
    [Theory]
    [InlineData("https://open.spotify.com/album/4aawyAB9vmqN3uQ7FjRGTy?si=abc", "4aawyAB9vmqN3uQ7FjRGTy")]
    [InlineData("spotify:album:4aawyAB9vmqN3uQ7FjRGTy", "4aawyAB9vmqN3uQ7FjRGTy")]
    [InlineData("4aawyAB9vmqN3uQ7FjRGTy", "4aawyAB9vmqN3uQ7FjRGTy")]
    [InlineData("Gorilla James Taylor", null)]
    public void Spotify_ParsesAlbumUrls(string input, string? expected)
    {
        using var spotify = new SpotifySource(null, null);
        Assert.Equal(expected, spotify.ParseId(input));
    }

    [Theory]
    [InlineData("https://www.discogs.com/release/1605685-Various-Tangokat", "r1605685")]
    [InlineData("https://www.discogs.com/da/release/1605685-Various-Tangokat", "r1605685")]
    [InlineData("[r1605685]", "r1605685")]
    [InlineData("1605685", "r1605685")]
    [InlineData("https://www.discogs.com/master/1265218-Various-Tangokat", "m1265218")]
    [InlineData("[m1265218]", "m1265218")]
    [InlineData("Tangokat", null)]
    public void Discogs_ParsesReleaseAndMasterUrls(string input, string? expected)
    {
        using var discogs = new DiscogsSource(null);
        Assert.Equal(expected, discogs.ParseId(input));
    }

    [Fact]
    public void MissingKeys_AreReportedBeforeSearching()
    {
        using var spotify = new SpotifySource("id", null);
        using var discogs = new DiscogsSource(" ");
        using var discogsWithToken = new DiscogsSource("token");

        Assert.NotNull(spotify.SearchProblem);
        Assert.NotNull(discogs.SearchProblem);
        Assert.Null(discogsWithToken.SearchProblem);
    }

    [Fact]
    public void SearchLimit_IsWhatEachSourceReturnsAtMost()
    {
        using var spotify = new SpotifySource(null, null);
        using var discogs = new DiscogsSource(null);

        Assert.Equal(10, spotify.SearchLimit);
        Assert.Equal(25, discogs.SearchLimit);
    }
}

public class TextRepairTests
{
    [Theory]
    [InlineData("SÃ¸ren", "Søren")]
    [InlineData("├ª├©├Ñ", "æøå")]
    [InlineData("KÃ¦rlighed", "Kærlighed")]
    public void FixesGarbledLetters(string text, string expected) => Assert.Equal(expected, TextRepair.Fix(text));

    [Theory]
    [InlineData("Søren")]
    [InlineData("Århus")]
    [InlineData("")]
    [InlineData(null)]
    public void LeavesCorrectTextAlone(string? text) => Assert.Null(TextRepair.Fix(text));

    [Fact]
    public void RecoversALostLetterFromAReference() =>
        Assert.Equal("Mine Øjne", TextRepair.Fix("Mine Ã jne", "18 Lis Sørensen - Mine Øjne"));
}
