namespace Mp3TagUpdater.Tests;

/// <summary>Writes small but valid mp3 files, so the tests need no audio files of their own.</summary>
internal static class TestMp3
{
    // MPEG-1 Layer III, 128 kbit/s, 44.1 kHz, no padding: 144 * 128000 / 44100 = 417 bytes per frame.
    private static readonly byte[] FrameHeader = [0xFF, 0xFB, 0x90, 0x64];
    private const int FrameSize = 417;
    private const int FrameCount = 100; // about 2.6 seconds

    public static string Create(string path, string? album = null, string? title = "Test Title", string? artist = "Test Artist")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var audio = new byte[FrameSize * FrameCount];
        for (var i = 0; i < FrameCount; i++)
            FrameHeader.CopyTo(audio, i * FrameSize);
        File.WriteAllBytes(path, audio);

        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Title = title;
            file.Tag.Performers = artist is null ? [] : [artist];
            if (album is not null)
                file.Tag.Album = album;
            file.Save();
        }

        return path;
    }
}
