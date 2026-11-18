using Mp3Tag.Albums;

namespace Mp3Tag.Tests.Albums;

public class TrackMatcherTests
{
    private static AudioFile File(string name, string? tagTitle = null) => new($@"C:\music\{name}") { Title = tagTitle };

    private static List<AlbumTrack> Tracks(params string[] titles) =>
        titles.Select((t, i) => new AlbumTrack(1, i + 1, "Artist", t, null)).ToList();

    private static string[] FileNamesInTrackOrder(TrackMatch m) => m.Pairs.Select(p => p.File.FileName).ToArray();

    [Fact]
    public void MatchesByTitleTag_WhateverTheFileOrder()
    {
        var files = new[] { File("a.mp3", "Music"), File("b.mp3", "Mexico"), File("c.mp3", "Gorilla") };
        var m = TrackMatcher.Match(files, Tracks("Mexico", "Music", "Gorilla"));

        Assert.Equal(MatchMethod.TrackName, m.Method);
        Assert.Equal(["b.mp3", "a.mp3", "c.mp3"], FileNamesInTrackOrder(m));
    }

    [Fact]
    public void MatchesFileNamesWithNumberAndArtist()
    {
        var files = new[] { File("07 - Gnags - Bjørnen Sover.mp3"), File("01 - Monique - Min Kat Den Danser Tango.mp3") };
        var m = TrackMatcher.Match(files, Tracks("Min Kat Den Danser Tango", "Bjørnen Sover"));

        Assert.Equal(MatchMethod.TrackName, m.Method);
        Assert.Equal(["01 - Monique - Min Kat Den Danser Tango.mp3", "07 - Gnags - Bjørnen Sover.mp3"], FileNamesInTrackOrder(m));
    }

    [Fact]
    public void IgnoresCaseAccentsAndPunctuation()
    {
        var files = new[] { File("x.mp3", "Go'Màren, Go'Màren"), File("y.mp3", "How Sweet it is (to be Loved by You)") };
        var m = TrackMatcher.Match(files, Tracks("How Sweet It Is (To Be Loved By You)", "Go'Måren, Go'Måren"));

        Assert.Equal(MatchMethod.TrackName, m.Method);
        Assert.Equal(["y.mp3", "x.mp3"], FileNamesInTrackOrder(m));
    }

    [Fact]
    public void IgnoresRemasterAndLiveNotes()
    {
        var files = new[] { File("02 Music.mp3"), File("01 Mexico.mp3") };
        var m = TrackMatcher.Match(files, Tracks("Mexico - 2019 Remaster", "Music (Live)"));

        Assert.Equal(MatchMethod.TrackName, m.Method);
        Assert.Equal(["01 Mexico.mp3", "02 Music.mp3"], FileNamesInTrackOrder(m));
    }

    [Fact]
    public void GenericNames_UseNaturalFileOrder()
    {
        var files = new[] { File("Track 10.mp3", "Track 10"), File("Track 2.mp3", "Track 2"), File("Track 1.mp3", "Track 1") };
        var m = TrackMatcher.Match(files, Tracks("One", "Two", "Three"));

        Assert.Equal(MatchMethod.FileOrder, m.Method);
        Assert.Equal(0, m.NameMatches);
        Assert.Equal(["Track 1.mp3", "Track 2.mp3", "Track 10.mp3"], FileNamesInTrackOrder(m));
    }

    [Fact]
    public void SomeNamesMatching_IsNotEnough_UsesFileOrder()
    {
        var files = new[] { File("01. Track 1.mp3", "Mexico"), File("02. Track 2.mp3", "Track 2"), File("03. Track 3.mp3") };
        var m = TrackMatcher.Match(files, Tracks("Music", "Mexico", "Gorilla"));

        Assert.Equal(MatchMethod.FileOrder, m.Method);
        Assert.Equal(1, m.NameMatches);
        Assert.Equal(["01. Track 1.mp3", "02. Track 2.mp3", "03. Track 3.mp3"], FileNamesInTrackOrder(m));
    }

    [Fact]
    public void DuplicateTitles_UseFileOrder()
    {
        var files = new[] { File("b.mp3", "Intro"), File("a.mp3", "Intro") };
        var m = TrackMatcher.Match(files, Tracks("Intro", "Intro"));

        Assert.Equal(MatchMethod.FileOrder, m.Method);
        Assert.Equal(["a.mp3", "b.mp3"], FileNamesInTrackOrder(m));
    }

    [Fact]
    public void EndsWithMatch_DoesNotStealAnExactMatch()
    {
        // "Love Songs" ends with "Songs", but both files have exact matches.
        var files = new[] { File("10 Love Songs.mp3"), File("11 Songs.mp3") };
        var m = TrackMatcher.Match(files, Tracks("Songs", "Love Songs"));

        Assert.Equal(MatchMethod.TrackName, m.Method);
        Assert.Equal(["11 Songs.mp3", "10 Love Songs.mp3"], FileNamesInTrackOrder(m));
    }

    [Fact]
    public void DifferentCounts_Throw() =>
        Assert.Throws<ArgumentException>(() => TrackMatcher.Match([File("a.mp3")], Tracks("A", "B")));

    [Theory]
    [InlineData("Track 1", true)]
    [InlineData("Track01", true)]
    [InlineData("01", true)]
    [InlineData("", true)]
    [InlineData("Spor 3", true)]
    [InlineData("Mexico", false)]
    [InlineData("99 Luftballons", false)]
    public void IsGeneric(string text, bool expected) => Assert.Equal(expected, TrackMatcher.IsGeneric(text));
}
