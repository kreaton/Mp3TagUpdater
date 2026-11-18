namespace Mp3Tag.Tests.Albums;

/// <summary>Writes small but valid mp3 files of a chosen length, so tests can tell files apart by length.</summary>
internal static class AlbumMp3
{
    // MPEG-1 Layer III, 128 kbit/s, 44.1 kHz, no padding: 144 * 128000 / 44100 = 417 bytes per frame (~26 ms).
    private static readonly byte[] FrameHeader = [0xFF, 0xFB, 0x90, 0x64];
    private const int FrameSize = 417;

    /// <param name="frames">Length in frames; 100 frames is about 2.6 seconds.</param>
    public static string Create(string path, string? title = null, int frames = 100, string? artist = null, string? album = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var audio = new byte[FrameSize * frames];
        for (var i = 0; i < frames; i++)
            FrameHeader.CopyTo(audio, i * FrameSize);
        File.WriteAllBytes(path, audio);

        if (title is not null || artist is not null || album is not null)
        {
            using var file = TagLib.File.Create(path);
            file.Tag.Title = title;
            if (artist is not null) file.Tag.Performers = [artist];
            if (album is not null) file.Tag.Album = album;
            file.Save();
        }
        return path;
    }
}

/// <summary>A temporary folder that is deleted after the test.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder(string? name = null)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Mp3Tag.Tests", Guid.NewGuid().ToString("N"), name ?? "Album");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public string[] FileNames() =>
        Directory.GetFiles(Path).Select(System.IO.Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    public void Dispose()
    {
        try { Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, recursive: true); } catch (IOException) { }
    }
}
