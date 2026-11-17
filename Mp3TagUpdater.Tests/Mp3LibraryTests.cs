using Mp3TagUpdater.Models;
using Mp3TagUpdater.Services;

namespace Mp3TagUpdater.Tests;

public sealed class Mp3LibraryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mp3tagupdater-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Tagged_file_is_ok_and_unchanged()
    {
        var path = TestMp3.Create(Path.Combine(_root, "Party", "song.mp3"), album: "Greatest Hits");

        var result = new Mp3Library().Run(new Mp3RunOptions(_root));

        var record = Assert.Single(result.Records);
        Assert.Equal(Mp3Status.Ok, record.Status);
        Assert.Equal("Greatest Hits", record.Album);
        Assert.Equal("Test Title", record.Title);
        Assert.True(record.Duration > TimeSpan.Zero);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void Dry_run_proposes_album_from_folder_without_writing()
    {
        var path = TestMp3.Create(Path.Combine(_root, "Party", "song.mp3"));
        var before = File.ReadAllBytes(path);
        var messages = new List<Mp3Message>();

        var result = new Mp3Library(messages.Add).Run(new Mp3RunOptions(_root));

        var record = Assert.Single(result.Records);
        Assert.Equal(Mp3Status.Updatable, record.Status);
        Assert.Equal(["Set Album to \"Party\""], record.Changes);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Contains(messages, m => m.Kind == Mp3MessageKind.Change && m.Text.StartsWith("[DRY RUN]"));
    }

    [Fact]
    public void Apply_fills_album_and_keeps_modified_time()
    {
        var path = TestMp3.Create(Path.Combine(_root, "Party", "song.mp3"));
        var modified = new DateTime(2020, 5, 17, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, modified);

        var result = new Mp3Library().Run(new Mp3RunOptions(_root) { DryRun = false });

        var record = Assert.Single(result.Records);
        Assert.Equal(Mp3Status.Updated, record.Status);
        Assert.Equal("Party", record.Album);
        Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
        Assert.False(File.Exists(path + ".bak"));

        using var file = TagLib.File.Create(path);
        Assert.Equal("Party", file.Tag.Album);
    }

    [Fact]
    public void Bad_version_byte_is_repairable_then_repaired_with_backup()
    {
        var path = TestMp3.Create(Path.Combine(_root, "Party", "broken.mp3"), album: "Live");
        var bytes = File.ReadAllBytes(path);
        bytes[3] = 0x23;
        File.WriteAllBytes(path, bytes);

        var dryRun = new Mp3Library().Run(new Mp3RunOptions(_root));
        Assert.Equal(Mp3Status.Repairable, Assert.Single(dryRun.Records).Status);
        Assert.Equal(bytes, File.ReadAllBytes(path));

        var applied = new Mp3Library().Run(new Mp3RunOptions(_root) { DryRun = false });
        var record = Assert.Single(applied.Records);
        Assert.Equal(Mp3Status.Repaired, record.Status);
        Assert.Equal("Live", record.Album);
        Assert.Equal(bytes, File.ReadAllBytes(path + ".bak"));
    }

    [Fact]
    public void Repaired_file_also_gets_missing_album()
    {
        var path = TestMp3.Create(Path.Combine(_root, "Party", "broken.mp3"));
        var bytes = File.ReadAllBytes(path);
        bytes[3] = 0x23;
        File.WriteAllBytes(path, bytes);

        var record = Assert.Single(new Mp3Library().Run(new Mp3RunOptions(_root) { DryRun = false }).Records);

        Assert.Equal(Mp3Status.Repaired, record.Status);
        Assert.Equal("Party", record.Album);
        Assert.Equal(2, record.Changes.Count);
    }

    [Fact]
    public void Unreadable_file_is_an_error_and_left_alone()
    {
        var path = Path.Combine(_root, "garbage.mp3");
        File.WriteAllBytes(path, [.. "ID3"u8, 0x09, 0, 0, 0, 0, 0, 10, 1, 2, 3]);

        var result = new Mp3Library().Run(new Mp3RunOptions(_root) { DryRun = false });

        var record = Assert.Single(result.Records);
        Assert.Contains(record.Status, new[] { Mp3Status.Error, Mp3Status.Warning });
        Assert.Empty(record.Changes);
    }

    [Fact]
    public void Non_recursive_scan_skips_subfolders()
    {
        TestMp3.Create(Path.Combine(_root, "top.mp3"), album: "A");
        TestMp3.Create(Path.Combine(_root, "Sub", "nested.mp3"), album: "B");

        var result = new Mp3Library().Run(new Mp3RunOptions(_root) { Recursive = false });

        Assert.Equal("top.mp3", Assert.Single(result.Records).FileName);
    }

    [Fact]
    public void Cancelled_run_changes_nothing()
    {
        var path = TestMp3.Create(Path.Combine(_root, "Party", "song.mp3"));
        var before = File.ReadAllBytes(path);

        var result = new Mp3Library().Run(new Mp3RunOptions(_root) { DryRun = false }, cancellation: new CancellationToken(canceled: true));

        Assert.True(result.Cancelled);
        Assert.Empty(result.Records);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Progress_reports_every_file()
    {
        TestMp3.Create(Path.Combine(_root, "a.mp3"), album: "A");
        TestMp3.Create(Path.Combine(_root, "b.mp3"), album: "B");
        var reports = new List<Mp3Progress>();

        new Mp3Library().Run(new Mp3RunOptions(_root), new ListProgress(reports));

        Assert.Contains(new Mp3Progress("Reading tags", 2, 2), reports);
    }

    [Fact]
    public void Csv_log_has_a_header_and_one_row_per_file()
    {
        TestMp3.Create(Path.Combine(_root, "Party", "song, with comma.mp3"));
        var result = new Mp3Library().Run(new Mp3RunOptions(_root));

        var logPath = Mp3CsvLog.Write(result.Records, _root);
        var lines = File.ReadAllLines(logPath);

        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Status,Errors,Changes,RelativePath", lines[0]);
        Assert.StartsWith("Updatable,,", lines[1]);
        Assert.Contains("\"song, with comma.mp3\"", lines[1]);
    }

    [Fact]
    public void Letters_and_artist_are_fixed_only_when_asked()
    {
        TestMp3.Create(Path.Combine(_root, "Hits", "01 Tøsedrengene - Indianer.mp3"), album: "Hits",
            title: "TÃ¸sedrengene / Indianer", artist: "Various");

        var plain = Assert.Single(new Mp3Library().Run(new Mp3RunOptions(_root)).Records);
        Assert.Equal(Mp3Status.Ok, plain.Status);
        Assert.Empty(plain.Changes);

        var options = new Mp3RunOptions(_root) { FixLetters = true, SplitTitles = TitleSplitMode.ArtistFirst };
        var dryRun = Assert.Single(new Mp3Library().Run(options).Records);
        Assert.Equal(Mp3Status.Updatable, dryRun.Status);
        Assert.Equal("Indianer", dryRun.Edit?.Title);
        Assert.Equal("Tøsedrengene", dryRun.Edit?.Artist);
        Assert.True(dryRun.Edit is { FixesLetters: true, SplitsTitle: true, FillsAlbum: false, FileName: null });
    }

    [Fact]
    public void Apply_writes_fixed_tags_renames_and_keeps_modified_time()
    {
        var path = TestMp3.Create(Path.Combine(_root, "Disney", "06 VÃœr vor gÃœst.mp3"), album: "Disney",
            title: "VÃ¦r vor gÃ¦st", artist: "Various");
        var modified = new DateTime(2020, 5, 17, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, modified);

        var result = new Mp3Library().Run(new Mp3RunOptions(_root) { DryRun = false, FixLetters = true });

        var record = Assert.Single(result.Records);
        Assert.Equal(Mp3Status.Updated, record.Status);
        Assert.Equal("06 Vær vor gæst.mp3", record.FileName);
        Assert.Equal("Vær vor gæst", record.Title);
        Assert.False(File.Exists(path));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(record.FilePath));

        using var file = TagLib.File.Create(record.FilePath);
        Assert.Equal("Vær vor gæst", file.Tag.Title);
    }

    [Fact]
    public void Rename_never_replaces_an_existing_file()
    {
        TestMp3.Create(Path.Combine(_root, "Hits", "BÃ¥d.mp3"), album: "Hits", title: "Båd");
        TestMp3.Create(Path.Combine(_root, "Hits", "Båd.mp3"), album: "Hits", title: "Båd");

        var result = new Mp3Library().Run(new Mp3RunOptions(_root) { DryRun = false, FixLetters = true });

        var failed = Assert.Single(result.Records, r => r.Status == Mp3Status.Error);
        Assert.Contains(failed.Errors, e => e.Contains("already exists"));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_root, "Hits"), "*.mp3").Length);
    }

    [Fact]
    public void Split_order_is_worked_out_per_folder()
    {
        TestMp3.Create(Path.Combine(_root, "A", "1.mp3"), album: "A", title: "Song One / Thomas Helmig", artist: "Various");
        TestMp3.Create(Path.Combine(_root, "A", "2.mp3"), album: "A", title: "Song Two / Thomas Helmig", artist: "Various");
        TestMp3.Create(Path.Combine(_root, "B", "1.mp3"), album: "B", title: "Duran Duran / Ordinary World", artist: "Various");
        TestMp3.Create(Path.Combine(_root, "B", "2.mp3"), album: "B", title: "Duran Duran / Come Undone", artist: "Various");

        var records = new Mp3Library().Run(new Mp3RunOptions(_root) { SplitTitles = TitleSplitMode.Auto }).Records;

        Assert.All(records.Where(r => r.ContainingDirectoryName == "A"), r => Assert.Equal("Thomas Helmig", r.Edit?.Artist));
        Assert.All(records.Where(r => r.ContainingDirectoryName == "B"), r => Assert.Equal("Duran Duran", r.Edit?.Artist));
    }

    private sealed class ListProgress(List<Mp3Progress> reports) : IProgress<Mp3Progress>
    {
        public void Report(Mp3Progress value) => reports.Add(value);
    }
}
