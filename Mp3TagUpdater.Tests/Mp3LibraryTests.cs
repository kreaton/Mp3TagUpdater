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

    private sealed class ListProgress(List<Mp3Progress> reports) : IProgress<Mp3Progress>
    {
        public void Report(Mp3Progress value) => reports.Add(value);
    }
}
