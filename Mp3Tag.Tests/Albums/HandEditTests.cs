using Mp3Tag.Albums;

namespace Mp3Tag.Tests.Albums;

public class HandEditTests
{
    private static AlbumInfo Album(params string[] titles) => new()
    {
        Source = "Test",
        Id = "42",
        Url = "https://example.invalid/42",
        Artist = "James Taylor",
        Title = "Gorilla",
        Year = 1975,
        Tracks = titles.Select((t, i) => new AlbumTrack(1, i + 1, "James Taylor", t, null)).ToList(),
    };

    private static Dictionary<string, TagChange> Edits(string path, TagChange edit) =>
        new(StringComparer.OrdinalIgnoreCase) { [path] = edit };

    [Fact]
    public void EditedTitle_TakesThePlaceOfTheAlbums_AndTheNameFollows()
    {
        using var dir = new TempFolder();
        AlbumMp3.Create(dir.File("Track 1.mp3"), "Track 1");
        AlbumMp3.Create(dir.File("Track 2.mp3"), "Track 2");
        var plan = AlbumTagger.Plan(dir.Path, Album("Mexico", "Music"), FolderReader.Read(dir.Path));
        var second = plan.Files[1].File.Path;

        var edited = AlbumTagger.WithHandEdits(plan, Edits(second, new TagChange { Title = "Music (Live)" }));
        AlbumTagger.Apply(edited);

        var file = edited.Files[1];
        Assert.True(file.EditedByHand);
        Assert.False(edited.Files[0].EditedByHand);
        Assert.Contains("Title: Track 2 → Music (Live) (by hand)", file.Changes);
        Assert.Contains("Artist: James Taylor", file.Changes); // the album's values stay
        Assert.Equal(["01 - Mexico.mp3", "02 - Music (Live).mp3"], dir.FileNames());
        using var written = TagLib.File.Create(dir.File("02 - Music (Live).mp3"));
        Assert.Equal("Music (Live)", written.Tag.Title);
        Assert.Equal("Gorilla", written.Tag.Album);
    }

    [Fact]
    public void FilesOwnValue_TurnsDownTheAlbums()
    {
        using var dir = new TempFolder();
        AlbumMp3.Create(dir.File("Track 1.mp3"), "Mexico (Live)");
        var plan = AlbumTagger.Plan(dir.Path, Album("Mexico"), FolderReader.Read(dir.Path));

        var edited = AlbumTagger.WithHandEdits(plan, Edits(plan.Files[0].File.Path, new TagChange { Title = "Mexico (Live)" }));

        var file = Assert.Single(edited.Files);
        Assert.DoesNotContain(file.Changes, c => c.StartsWith("Title:"));
        Assert.Null(file.Change.Title);
        Assert.Equal("01 - Mexico (Live).mp3", file.NewFileName);
    }

    [Fact]
    public void WithoutAnAlbum_OnlyTheEditsChange_AndEmptyValuesRemoveTags()
    {
        using var dir = new TempFolder();
        var path = AlbumMp3.Create(dir.File("Song.mp3"), "Song", artist: "Someone", album: "Old");
        using (var f = TagLib.File.Create(path)) { f.Tag.Year = 1999; f.Save(); }
        var files = FolderReader.Read(dir.Path);

        var plan = AlbumTagger.WithHandEdits(AlbumTagger.PlanNothing(dir.Path, files),
            Edits(files[0].Path, new TagChange { Album = "New", Artist = "", Year = 0 }));
        AlbumTagger.Apply(plan);

        var file = Assert.Single(plan.Changed);
        Assert.Equal(["Album: Old → New (by hand)", "Artist: Someone → (removed) (by hand)", "Year: 1999 → (removed) (by hand)"],
            file.Changes.Order(StringComparer.Ordinal));
        Assert.False(file.Renames);
        Assert.Equal(["Song.mp3"], dir.FileNames());
        using var written = TagLib.File.Create(path);
        Assert.Equal("New", written.Tag.Album);
        Assert.Empty(written.Tag.Performers);
        Assert.Equal(0u, written.Tag.Year);
        Assert.Equal("Song", written.Tag.Title);
    }

    [Fact]
    public void NoEdits_LeaveThePlanAsItIs()
    {
        using var dir = new TempFolder();
        AlbumMp3.Create(dir.File("Track 1.mp3"), "Track 1");
        var plan = AlbumTagger.Plan(dir.Path, Album("Mexico"), FolderReader.Read(dir.Path));

        Assert.Same(plan, AlbumTagger.WithHandEdits(plan, new Dictionary<string, TagChange>()));
    }

    [Fact]
    public void Then_KeepsEarlierValuesThatTheLaterEditLeavesOut()
    {
        var earlier = new TagChange { Title = "A", Genre = "Rock" };
        var later = new TagChange { Genre = "Pop", Year = 2001 };

        var both = earlier.Then(later);

        Assert.Equal("A", both.Title);
        Assert.Equal("Pop", both.Genre);
        Assert.Equal(2001u, both.Year);
    }
}
