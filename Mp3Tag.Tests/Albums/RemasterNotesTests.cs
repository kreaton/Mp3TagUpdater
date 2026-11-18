using Mp3Tag.Albums;

namespace Mp3Tag.Tests.Albums;

public class RemasterNotesTests
{
    [Theory]
    [InlineData("Mexico - 2019 Remaster", "Mexico")]
    [InlineData("Gorilla (2019 Remaster)", "Gorilla")]
    [InlineData("Help! - Remastered 2009", "Help!")]
    [InlineData("Layla - 2011 Remastered Version", "Layla")]
    [InlineData("Wish You Were Here [Remastered]", "Wish You Were Here")]
    [InlineData("Abbey Road (Remastered Edition)", "Abbey Road")]
    [InlineData("How Sweet It Is (To Be Loved By You) - 2019 Remaster", "How Sweet It Is (To Be Loved By You)")]
    public void RemovesRemasterNotes(string title, string expected) => Assert.Equal(expected, RemasterNotes.Remove(title));

    [Theory]
    [InlineData("Fool (Live)")]
    [InlineData("Sarah Maria - Single Version")]
    [InlineData("Love Songs (Mono)")]
    [InlineData("Oppe I Norge - Fastelavn Er Mit Navn")]
    [InlineData("Remaster")]
    public void KeepsEverythingElse(string title) => Assert.Equal(title, RemasterNotes.Remove(title));

    [Fact]
    public void AlbumWithoutRemasterNotes_CleansAlbumAndTrackTitles()
    {
        var album = AlbumInfoTests.Album("James Taylor", ("James Taylor", "Mexico - 2019 Remaster"), ("James Taylor", "Fool (Live)"))
            with { Title = "Gorilla (2019 Remaster)" };

        var clean = album.WithoutRemasterNotes();

        Assert.Equal("Gorilla", clean.Title);
        Assert.Equal(["Mexico", "Fool (Live)"], clean.Tracks.Select(t => t.Title));
    }
}
