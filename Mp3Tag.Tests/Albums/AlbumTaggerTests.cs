using System.Text.Json;
using Mp3Tag.Albums;

namespace Mp3Tag.Tests.Albums;

public class AlbumTaggerTests
{
    private static AlbumInfo Album(string artist, string title, int year, params (string Artist, string Title)[] tracks) => new()
    {
        Source = "Test",
        Id = "42",
        Url = "https://example.invalid/42",
        Artist = artist,
        Title = title,
        Year = year,
        Genre = "Pop",
        Tracks = tracks.Select((t, i) => new AlbumTrack(1, i + 1, t.Artist, t.Title, null)).ToList(),
    };

    private static TagLib.File Open(string path) => TagLib.File.Create(path);

    private static FolderPlan PlanFor(TempFolder dir, AlbumInfo album, bool fixLetters = false) =>
        AlbumTagger.Plan(dir.Path, album, FolderReader.Read(dir.Path), fixLetters);

    [Fact]
    public void GenericFiles_AreTaggedAndRenamedInNaturalOrder()
    {
        using var dir = new TempFolder();
        AlbumMp3.Create(dir.File("Track 1.mp3"), "Track 1", frames: 100);
        AlbumMp3.Create(dir.File("Track 2.mp3"), "Track 2", frames: 100);
        AlbumMp3.Create(dir.File("Track 10.mp3"), "Track 10", frames: 300); // longest: must become track 3
        var album = Album("James Taylor", "Gorilla", 1975, ("James Taylor", "Mexico"), ("James Taylor", "Music"), ("James Taylor", "Gorilla"));

        var plan = PlanFor(dir, album);
        AlbumTagger.Apply(plan);

        Assert.Equal(MatchMethod.FileOrder, plan.Method);
        Assert.Equal(["01 - Mexico.mp3", "02 - Music.mp3", "03 - Gorilla.mp3"], dir.FileNames());

        using var third = Open(dir.File("03 - Gorilla.mp3"));
        Assert.True(third.Properties.Duration > TimeSpan.FromSeconds(5));
        Assert.Equal("Gorilla", third.Tag.Title);
        Assert.Equal(["James Taylor"], third.Tag.Performers);
        Assert.Equal(["James Taylor"], third.Tag.AlbumArtists);
        Assert.Equal("Gorilla", third.Tag.Album);
        Assert.Equal(3u, third.Tag.Track);
        Assert.Equal(3u, third.Tag.TrackCount);
        Assert.Equal(1975u, third.Tag.Year);
        Assert.Equal(["Pop"], third.Tag.Genres);
        var id3 = (TagLib.Id3v2.Tag)third.GetTag(TagLib.TagTypes.Id3v2);
        Assert.Equal(3, id3.Version);
        Assert.Empty(id3.GetFrames("TCMP"));
    }

    [Fact]
    public void Plan_DescribesEachChange()
    {
        using var dir = new TempFolder();
        AlbumMp3.Create(dir.File("Track 1.mp3"), "Track 1");

        var file = Assert.Single(PlanFor(dir, Album("James Taylor", "Gorilla", 1975, ("James Taylor", "Mexico"))).Files);

        Assert.Contains("Title: Track 1 → Mexico", file.Changes);
        Assert.Contains("Artist: James Taylor", file.Changes);
        Assert.Contains("Track: 1", file.Changes);
        Assert.Contains("Name: Track 1.mp3 → 01 - Mexico.mp3", file.Changes);
        Assert.Equal("01 - Mexico.mp3", file.NewFileName);
    }

    [Fact]
    public void FilesThatAlreadyMatch_AreLeftAlone()
    {
        using var dir = new TempFolder();
        AlbumMp3.Create(dir.File("Track 1.mp3"), "Track 1");
        var album = Album("James Taylor", "Gorilla", 1975, ("James Taylor", "Mexico"));
        AlbumTagger.Apply(PlanFor(dir, album));
        var written = File.GetLastWriteTimeUtc(dir.File("01 - Mexico.mp3"));

        var again = PlanFor(dir, album);

        Assert.Empty(again.Changed);
        AlbumTagger.Apply(again);
        Assert.Equal(written, File.GetLastWriteTimeUtc(dir.File("01 - Mexico.mp3")));
    }

    [Fact]
    public void FilesThatSwapNames_AreRenamedWithoutCollision()
    {
        using var dir = new TempFolder();
        AlbumMp3.Create(dir.File("01 - Music.mp3"), "Music", frames: 100);
        AlbumMp3.Create(dir.File("02 - Mexico.mp3"), "Mexico", frames: 300);
        var album = Album("James Taylor", "Gorilla", 1975, ("James Taylor", "Mexico"), ("James Taylor", "Music"));

        var plan = PlanFor(dir, album);
        AlbumTagger.Apply(plan);

        Assert.Equal(MatchMethod.TrackName, plan.Method);
        Assert.Equal(["01 - Mexico.mp3", "02 - Music.mp3"], dir.FileNames());
        using var mexico = Open(dir.File("01 - Mexico.mp3"));
        Assert.Equal("Mexico", mexico.Tag.Title);
        Assert.True(mexico.Properties.Duration > TimeSpan.FromSeconds(5)); // the file that was "02 - Mexico"
    }

    [Fact]
    public void Compilation_PutsArtistInNameAndSetsCompilationTags()
    {
        using var dir = new TempFolder();
        AlbumMp3.Create(dir.File("01. Track 1.mp3"));
        AlbumMp3.Create(dir.File("02. Track 2.mp3"));
        var album = Album("Various", "Pa-Papegøje!", 1994, ("Sanne Salomonsen", "Jeg Er En Papegøje Fra Amerika"), ("Niels-Henning Ørsted Pedersen", "Se Dig For / På En Grøn Bakketop"));

        AlbumTagger.Apply(PlanFor(dir, album));

        Assert.Equal(
            ["01 - Sanne Salomonsen - Jeg Er En Papegøje Fra Amerika.mp3", "02 - Niels-Henning Ørsted Pedersen - Se Dig For - På En Grøn Bakketop.mp3"],
            dir.FileNames());
        using var second = Open(dir.File(dir.FileNames()[1]));
        Assert.Equal("Se Dig For / På En Grøn Bakketop", second.Tag.Title); // the tag keeps the "/"
        Assert.Equal(["Niels-Henning Ørsted Pedersen"], second.Tag.Performers);
        Assert.Equal(["Various Artists"], second.Tag.AlbumArtists);
        var id3 = (TagLib.Id3v2.Tag)second.GetTag(TagLib.TagTypes.Id3v2);
        Assert.Single(id3.GetFrames("TCMP"));
        Assert.True(FolderReader.ReadFile(dir.File(dir.FileNames()[1])).IsCompilation);
    }

    [Fact]
    public void MultiDisc_NumbersPerDisc()
    {
        using var dir = new TempFolder();
        foreach (var n in new[] { 1, 2, 3 }) AlbumMp3.Create(dir.File($"Track {n}.mp3"));
        var album = Album("Artist", "Double", 2000) with
        {
            Tracks = [new(1, 1, "Artist", "A", null), new(1, 2, "Artist", "B", null), new(2, 1, "Artist", "C", null)],
        };

        AlbumTagger.Apply(PlanFor(dir, album));

        Assert.Equal(["1-01 - A.mp3", "1-02 - B.mp3", "2-01 - C.mp3"], dir.FileNames());
        using var c = Open(dir.File("2-01 - C.mp3"));
        Assert.Equal(2u, c.Tag.Disc);
        Assert.Equal(2u, c.Tag.DiscCount);
        Assert.Equal(1u, c.Tag.Track);
        Assert.Equal(1u, c.Tag.TrackCount);
    }

    [Fact]
    public void KeepsCreatedAndModifiedDates()
    {
        using var dir = new TempFolder();
        var path = AlbumMp3.Create(dir.File("Track 1.mp3"));
        var created = new DateTime(2000, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var modified = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetCreationTimeUtc(path, created);
        File.SetLastWriteTimeUtc(path, modified);

        AlbumTagger.Apply(PlanFor(dir, Album("A", "B", 0, ("A", "Song"))));

        Assert.Equal(created, File.GetCreationTimeUtc(dir.File("01 - Song.mp3")));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(dir.File("01 - Song.mp3")));
    }

    [Fact]
    public void WrongTrackCount_Throws()
    {
        using var dir = new TempFolder();
        AlbumMp3.Create(dir.File("Track 1.mp3"));
        var album = Album("A", "B", 0, ("A", "One"), ("A", "Two"));

        Assert.Throws<InvalidOperationException>(() => PlanFor(dir, album));
    }

    [Fact]
    public void Read_OnlyReadsMp3Files_AndListsUnreadableOnes()
    {
        using var dir = new TempFolder();
        AlbumMp3.Create(dir.File("Track 1.mp3"));
        File.WriteAllText(dir.File("cover.jpg"), "x");
        File.WriteAllText(dir.File("Album.json"), "{}");
        File.WriteAllBytes(dir.File("Track 2.mp3"), new byte[64]);

        var files = FolderReader.Read(dir.Path);

        Assert.Equal(["Track 1.mp3", "Track 2.mp3"], files.Select(f => f.FileName));
        Assert.Null(files[0].Error);
        Assert.NotNull(files[1].Error);
    }

    [Fact]
    public void UnreadableFile_IsNeverChanged()
    {
        using var dir = new TempFolder();
        AlbumMp3.Create(dir.File("Track 1.mp3"));
        File.WriteAllBytes(dir.File("Track 2.mp3"), new byte[64]);

        var plan = PlanFor(dir, Album("A", "B", 0, ("A", "One"), ("A", "Two")));

        Assert.Single(plan.Unreadable);
        Assert.Empty(plan.Unreadable[0].Changes);
    }

    [Fact]
    public void GarbledTitles_StillMatchTheirTracks()
    {
        using var dir = new TempFolder();
        AlbumMp3.Create(dir.File("a.mp3"), "Bjørnen Sover");
        AlbumMp3.Create(dir.File("b.mp3"), "SÃ¸ren Banjomus");

        var plan = PlanFor(dir, Album("Various", "Tangokat", 1995, ("Gnags", "Søren Banjomus"), ("Monique", "Bjørnen Sover")));

        Assert.Equal(MatchMethod.TrackName, plan.Method);
        Assert.Equal(["b.mp3", "a.mp3"], plan.Files.Select(f => f.File.FileName));
    }

    [Fact]
    public void LetterFixes_CorrectTagsAndNamesOnly()
    {
        using var dir = new TempFolder("KÃ¦rlighed");
        AlbumMp3.Create(dir.File("01 SÃ¸ren.mp3"), "SÃ¸ren", artist: "Lis SÃ¸rensen", album: "KÃ¦rlighed");
        AlbumMp3.Create(dir.File("02 Fine.mp3"), "Fine", artist: "Gnags", album: "Kærlighed");

        var plan = AlbumTagger.PlanLetterFixes(dir.Path, FolderReader.Read(dir.Path));
        AlbumTagger.Apply(plan);

        Assert.Null(plan.Album);
        Assert.Single(plan.Changed);
        Assert.Equal(["01 Søren.mp3", "02 Fine.mp3"], dir.FileNames());
        var fixedFile = FolderReader.ReadFile(dir.File("01 Søren.mp3"));
        Assert.Equal("Søren", fixedFile.Title);
        Assert.Equal("Lis Sørensen", fixedFile.Artist);
        Assert.Equal("Kærlighed", fixedFile.Album);
        Assert.Equal(0u, fixedFile.Track); // nothing but the letters changes
    }

    [Fact]
    public void AlbumJson_UsesTheAlbumNameAsAValidFileName_AndDescribesTheFiles()
    {
        using var dir = new TempFolder();
        AlbumMp3.Create(dir.File("Track 1.mp3"), "baba o riley");   // matched by title, renamed and retitled
        AlbumMp3.Create(dir.File("02 - Bargain.mp3"), "Bargain");   // already right
        var album = Album("The Who", "Who's Next?", 1971) with
        {
            Tracks = [new(1, 1, "The Who", "Baba O'Riley", TimeSpan.FromSeconds(298)), new(1, 2, "The Who", "Bargain", null)],
        };

        var plan = PlanFor(dir, album);
        AlbumTagger.Apply(plan);
        var path = AlbumJson.Write(plan);

        Assert.Equal("Who's Next.json", Path.GetFileName(path));
        Assert.Equal(dir.Path, Path.GetDirectoryName(path));
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("Who's Next?", root.GetProperty("album").GetString());
        Assert.Equal(1971, root.GetProperty("year").GetInt32());
        Assert.Equal("title", root.GetProperty("matchedBy").GetString());

        var first = root.GetProperty("tracks")[0];
        Assert.Equal("Baba O'Riley", first.GetProperty("title").GetString());
        Assert.Equal("4:58", first.GetProperty("duration").GetString());
        Assert.Equal("01 - Baba O'Riley.mp3", first.GetProperty("file").GetString());
        Assert.Equal("Track 1.mp3", first.GetProperty("originalFile").GetString());
        Assert.Equal("baba o riley", first.GetProperty("originalTitle").GetString());
        Assert.Equal("0:02", first.GetProperty("fileDuration").GetString());

        var second = root.GetProperty("tracks")[1];
        Assert.Equal("02 - Bargain.mp3", second.GetProperty("file").GetString());
        Assert.False(second.TryGetProperty("originalFile", out _));  // not renamed
        Assert.False(second.TryGetProperty("originalTitle", out _)); // title unchanged
        Assert.False(second.TryGetProperty("duration", out _));      // source had no length
    }
}
