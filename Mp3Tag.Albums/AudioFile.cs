namespace Mp3Tag.Albums;

/// <summary>An mp3 file in the folder, with the tags it has now.</summary>
public sealed record AudioFile(string Path)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? AlbumArtist { get; init; }
    public string? Album { get; init; }
    public string? Genre { get; init; }
    public uint Track { get; init; }
    public uint TrackCount { get; init; }
    public uint Disc { get; init; }
    public uint DiscCount { get; init; }
    public uint Year { get; init; }

    /// <summary>The ID3v2 TCMP "part of a compilation" flag is set.</summary>
    public bool IsCompilation { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>Why the file couldn't be read, or null. Such a file is listed but never changed.</summary>
    public string? Error { get; init; }
}

/// <summary>Reads the mp3 files of one folder with TagLibSharp.</summary>
public static class FolderReader
{
    /// <summary>The folder's .mp3 files (not subfolders), sorted by name the way Explorer does ("2" before "10").</summary>
    public static IReadOnlyList<AudioFile> Read(string folder, CancellationToken cancellation = default)
    {
        var paths = Directory.EnumerateFiles(System.IO.Path.GetFullPath(folder))
            .Where(p => System.IO.Path.GetExtension(p).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            .OrderBy(System.IO.Path.GetFileName, NaturalComparer.Instance)
            .ToList();

        var files = new List<AudioFile>(paths.Count);
        foreach (var path in paths)
        {
            cancellation.ThrowIfCancellationRequested();
            files.Add(ReadFile(path));
        }
        return files;
    }

    public static AudioFile ReadFile(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            var id3 = file.GetTag(TagLib.TagTypes.Id3v2) as TagLib.Id3v2.Tag;
            return new AudioFile(path)
            {
                Title = Clean(tag.Title),
                Artist = Clean(string.Join(", ", tag.Performers)),
                AlbumArtist = Clean(string.Join(", ", tag.AlbumArtists)),
                Album = Clean(tag.Album),
                Genre = Clean(tag.FirstGenre),
                Track = tag.Track,
                TrackCount = tag.TrackCount,
                Disc = tag.Disc,
                DiscCount = tag.DiscCount,
                Year = tag.Year,
                IsCompilation = id3?.GetFrames("TCMP").Any() == true,
                Duration = file.Properties?.Duration ?? TimeSpan.Zero,
            };
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or UnauthorizedAccessException)
        {
            return new AudioFile(path) { Error = ex.Message };
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
