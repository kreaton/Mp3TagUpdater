using System.Globalization;
using System.Text;
using Mp3TagUpdater.Models;

namespace Mp3TagUpdater.Services;

/// <summary>
/// Writes the scan results to an RFC 4180 CSV file in UTF-8 with a BOM, which
/// lets Excel detect the encoding. Numbers are whole and dates are ISO 8601, so
/// values import the same way under any locale. Empty cells mean "no value".
/// </summary>
public static class Mp3CsvLog
{
    private static readonly (string Header, Func<Mp3TagRecord, string, object?> Value)[] Columns =
    [
        ("Status", (r, _) => r.Status),
        ("Errors", (r, _) => string.Join(" | ", r.Errors)),
        ("RepairActions", (r, _) => string.Join(" | ", r.RepairActions)),
        ("RelativePath", (r, root) => Path.GetRelativePath(root, r.FilePath)),
        ("Directory", (r, _) => r.ContainingDirectoryName),
        ("FileName", (r, _) => r.FileName),
        ("FullPath", (r, _) => r.FilePath),
        ("SizeBytes", (r, _) => r.SizeBytes),
        ("Created", (r, _) => r.Created.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
        ("Modified", (r, _) => r.Modified.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
        ("Title", (r, _) => r.Title),
        ("Artist", (r, _) => r.Artist),
        ("AlbumArtist", (r, _) => r.AlbumArtist),
        ("Album", (r, _) => r.Album),
        ("Genre", (r, _) => r.Genre),
        ("Composer", (r, _) => r.Composer),
        ("Comment", (r, _) => r.Comment),
        ("Year", (r, _) => ZeroAsEmpty(r.Year)),
        ("Track", (r, _) => ZeroAsEmpty(r.Track)),
        ("TrackCount", (r, _) => ZeroAsEmpty(r.TrackCount)),
        ("Disc", (r, _) => ZeroAsEmpty(r.Disc)),
        ("Duration", (r, _) => r.Duration > TimeSpan.Zero ? r.Duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : null),
        ("DurationMs", (r, _) => r.Duration > TimeSpan.Zero ? (long)r.Duration.TotalMilliseconds : null),
        ("BitrateKbps", (r, _) => ZeroAsEmpty((uint)r.BitrateKbps)),
        ("SampleRateHz", (r, _) => ZeroAsEmpty((uint)r.SampleRateHz)),
        ("Channels", (r, _) => ZeroAsEmpty((uint)r.Channels)),
        ("TagTypes", (r, _) => r.TagTypes),
        ("PictureCount", (r, _) => r.PictureCount),
    ];

    /// <summary>
    /// Writes mp3-log-YYYYMMDD-HHMMSS.csv to <paramref name="rootDirectory"/>
    /// and returns the full path of the new file.
    /// </summary>
    public static string Write(IEnumerable<Mp3TagRecord> records, string rootDirectory)
    {
        var fileName = $"mp3-log-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.csv";
        var path = Path.Combine(rootDirectory, fileName);

        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.NewLine = "\r\n";

        writer.WriteLine(string.Join(',', Columns.Select(c => c.Header)));
        foreach (var record in records)
            writer.WriteLine(string.Join(',', Columns.Select(c => Escape(c.Value(record, rootDirectory)))));

        return path;
    }

    private static uint? ZeroAsEmpty(uint value) => value == 0 ? null : value;

    private static string Escape(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

        // Tags can hold NULs and line breaks, which many importers reject.
        text = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());

        bool needsQuotes = text.Contains(',') || text.Contains('"') || text != text.Trim();
        return needsQuotes ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }
}
