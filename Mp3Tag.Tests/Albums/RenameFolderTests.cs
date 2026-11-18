using Mp3Tag.Albums;

namespace Mp3Tag.Tests.Albums;

public class RenameFolderTests
{
    [Fact]
    public void Folder_IsRenamedWithItsFiles()
    {
        using var dir = new TempFolder("Far Mor Skru' Op");
        AlbumMp3.Create(dir.File("01 - Song.mp3"), "Song");

        var renamed = AlbumTagger.RenameFolder(dir.Path, "Far, Mor Vil Ikk' Skru' Op!");

        Assert.Equal(Path.Combine(Path.GetDirectoryName(dir.Path)!, "Far, Mor Vil Ikk' Skru' Op!"), renamed);
        Assert.False(Directory.Exists(dir.Path));
        Assert.True(File.Exists(Path.Combine(renamed, "01 - Song.mp3")));
    }

    [Fact]
    public void ChangeOfCaseOnly_IsRenamed()
    {
        using var dir = new TempFolder("abba gold");

        var renamed = AlbumTagger.RenameFolder(dir.Path, "ABBA Gold");

        var parent = Path.GetDirectoryName(dir.Path)!;
        Assert.Equal(["ABBA Gold"], Directory.GetDirectories(parent).Select(Path.GetFileName));
        Assert.Equal(Path.Combine(parent, "ABBA Gold"), renamed);
    }

    [Fact]
    public void TakenName_IsRefused_AndNothingMoves()
    {
        using var dir = new TempFolder("Old");
        var taken = Path.Combine(Path.GetDirectoryName(dir.Path)!, "New");
        Directory.CreateDirectory(taken);

        Assert.Throws<IOException>(() => AlbumTagger.RenameFolder(dir.Path, "New"));
        Assert.True(Directory.Exists(dir.Path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AC/DC")]
    [InlineData("What?")]
    [InlineData("Ends with a dot.")]
    [InlineData("CON")]
    public void InvalidNames_AreRefused(string name)
    {
        using var dir = new TempFolder("Old");

        Assert.Throws<ArgumentException>(() => AlbumTagger.RenameFolder(dir.Path, name));
        Assert.True(Directory.Exists(dir.Path));
    }
}
