using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mp3Tag.Albums;

/// <summary>
/// Saves the album information next to the music, as "&lt;album name&gt;.json": what the source said about the
/// album, and for each track which mp3 file it is, what it was called before and how long it really is.
/// </summary>
public static class AlbumJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep æ, ø, å readable
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The album title made safe as a file name, e.g. "Who's Next?" → "Who's Next.json".</summary>
    public static string FileNameFor(AlbumInfo album) => FileNames.Sanitize(album.Title, fallback: "album") + ".json";

    /// <summary>Writes the file into the plan's folder and returns its full path. Call after the plan is applied.</summary>
    /// <exception cref="InvalidOperationException">The plan has no album.</exception>
    public static string Write(FolderPlan plan)
    {
        var album = plan.Album ?? throw new InvalidOperationException("Only a plan made from an album can be saved as album JSON.");
        var doc = new
        {
            source = album.Source,
            id = album.Id,
            url = album.Url,
            artist = album.Artist,
            albumArtist = album.AlbumArtist,
            album = album.Title,
            year = album.Year > 0 ? album.Year : (int?)null,
            genre = album.Genre,
            label = album.Label,
            catalogNumber = album.CatalogNumber,
            country = album.Country,
            compilation = album.IsCompilation,
            matchedBy = plan.Method == MatchMethod.TrackName ? "title" : "fileOrder",
            tracks = plan.Files.Where(f => f.Track is not null).Select(f => new
            {
                disc = f.Track!.Disc,
                number = f.Track.Number,
                artist = f.Track.Artist,
                title = f.Track.Title,
                duration = f.Track.Duration is { } d ? Durations.Format(d) : null,
                file = f.NewFileName,
                originalFile = f.Renames ? f.File.FileName : null,
                originalTitle = string.Equals(f.File.Title, f.Track.Title, StringComparison.Ordinal) ? null : f.File.Title,
                fileDuration = Durations.Format(f.File.Duration),
            }),
        };

        var path = Path.Combine(plan.Folder, FileNameFor(album));
        File.WriteAllText(path, JsonSerializer.Serialize(doc, Options), new UTF8Encoding(false));
        return path;
    }
}
