using Mp3Tag.Albums;

namespace Mp3Tag.Tests.Albums;

public class AlbumFitTests
{
    private static AlbumInfo Album(params int?[] seconds) => new()
    {
        Source = "Test",
        Id = "1",
        Url = "https://example.invalid/1",
        Artist = "A",
        Title = "B",
        Tracks = seconds.Select((s, i) => new AlbumTrack(1, i + 1, "A", $"T{i + 1}", s is { } v ? TimeSpan.FromSeconds(v) : null)).ToList(),
    };

    private static AlbumFit Fit(int tracks, int? albumSeconds, int folderSeconds) =>
        new(tracks, 10, albumSeconds is { } a ? TimeSpan.FromSeconds(a) : null, TimeSpan.FromSeconds(folderSeconds));

    [Theory]
    [InlineData(10, 2400, 2400, FitLevel.Match)]
    [InlineData(10, 2440, 2400, FitLevel.Match)]          // 40 s off, within 2 % of 40 minutes (48 s)
    [InlineData(10, 2460, 2400, FitLevel.LengthDiffers)]  // 60 s off
    [InlineData(10, 200, 190, FitLevel.Match)]            // short folders get at least 15 s
    [InlineData(10, 200, 180, FitLevel.LengthDiffers)]
    [InlineData(10, null, 2400, FitLevel.TracksMatch)]    // no length listed: only the count is known
    [InlineData(12, 2400, 2400, FitLevel.TracksDiffer)]
    public void Level_ComparesTracksThenTotalLength(int tracks, int? albumSeconds, int folderSeconds, FitLevel expected)
    {
        Assert.Equal(expected, Fit(tracks, albumSeconds, folderSeconds).Level);
    }

    [Fact]
    public void TotalLength_IsUnknownWhenATrackHasNoLength()
    {
        Assert.Equal(TimeSpan.FromSeconds(300), AlbumFit.TotalLength(Album(100, 200)));
        Assert.Null(AlbumFit.TotalLength(Album(100, null)));
    }

    [Fact]
    public void Of_AddsUpTheFilesThatCanBeRead()
    {
        using var dir = new TempFolder();
        AlbumMp3.Create(dir.File("1.mp3"), "One", frames: 400);
        AlbumMp3.Create(dir.File("2.mp3"), "Two", frames: 400);
        var files = FolderReader.Read(dir.Path);

        var fit = AlbumFit.Of(Album(10, 11), files);

        Assert.Equal(2, fit.Files);
        Assert.Equal(files[0].Duration + files[1].Duration, fit.FolderLength);
        Assert.Equal(FitLevel.Match, fit.Level); // ~21 s against 21 s
    }
}
