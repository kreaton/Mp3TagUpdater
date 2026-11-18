using Mp3Tag.Albums;

namespace Mp3Tag.Tests.Albums;

public class FileNamesTests
{
    [Theory]
    [InlineData("Se Dig For / På En Grøn Bakketop", "Se Dig For - På En Grøn Bakketop")]
    [InlineData("Who's Next?", "Who's Next")]
    [InlineData("AC/DC: \"Live\"", "AC-DC- 'Live'")]
    [InlineData("  Trailing dots...  ", "Trailing dots")]
    [InlineData("Tabs\tand  spaces", "Tabs and spaces")]
    [InlineData("CON", "_CON")]
    [InlineData("???", "untitled")]
    public void Sanitize_MakesValidWindowsFileNames(string input, string expected) =>
        Assert.Equal(expected, FileNames.Sanitize(input));

    [Fact]
    public void Sanitize_LimitsLength() =>
        Assert.Equal(150, FileNames.Sanitize(new string('x', 300)).Length);
}

public class NaturalComparerTests
{
    [Fact]
    public void SortsNumbersByValue()
    {
        string[] names = ["Track 10.mp3", "Track 2.mp3", "track 1.mp3", "Track 02b.mp3"];
        Assert.Equal(["track 1.mp3", "Track 2.mp3", "Track 02b.mp3", "Track 10.mp3"], names.Order(NaturalComparer.Instance));
    }
}

public class DurationsTests
{
    [Theory]
    [InlineData("3:05", 185)]
    [InlineData("1:02:03", 3723)]
    public void TryParse_ReadsMinutesAndHours(string text, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), Durations.TryParse(text));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("3")]
    public void TryParse_ReturnsNullForMissingLengths(string? text) => Assert.Null(Durations.TryParse(text));

    [Fact]
    public void Format_ShowsMinutesAndSeconds() => Assert.Equal("4:09", Durations.Format(TimeSpan.FromSeconds(249.6)));
}

public class AlbumInfoTests
{
    [Fact]
    public void VariousIsACompilation() =>
        Assert.True(Album("Various", ("Monique", "A"), ("Gnags", "B")).IsCompilation);

    [Fact]
    public void SeriesWithOwnTrackArtistsIsACompilation()
    {
        var album = Album("Åh Abe!", ("Monique", "A"), ("Gnags", "B"), ("Nanna", "C"));
        Assert.True(album.IsCompilation);
        Assert.Equal("Various Artists", album.AlbumArtist);
    }

    [Fact]
    public void GuestOnOneTrackIsNotACompilation()
    {
        var album = Album("James Taylor", ("James Taylor", "A"), ("James Taylor, Carly Simon", "B"), ("James Taylor", "C"));
        Assert.False(album.IsCompilation);
        Assert.Equal("James Taylor", album.AlbumArtist);
    }

    internal static AlbumInfo Album(string artist, params (string Artist, string Title)[] tracks) => new()
    {
        Source = "Test",
        Id = "1",
        Url = "https://example.invalid/1",
        Artist = artist,
        Title = "Test Album",
        Tracks = tracks.Select((t, i) => new AlbumTrack(1, i + 1, t.Artist, t.Title, null)).ToList(),
    };
}
