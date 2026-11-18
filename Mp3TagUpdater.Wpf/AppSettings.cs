using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mp3Tag.Core;

namespace Mp3TagUpdater.Wpf;

/// <summary>
/// The choices remembered between runs, kept in %APPDATA%\Mp3TagUpdater\settings.json.
/// The folder lives outside the install folder, so it survives updates.
/// </summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mp3TagUpdater", "settings.json");

    // Enums as names ("Auto"), so the file is readable and survives reordering the enum.
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>The folder of the last scan.</summary>
    public string? Folder { get; set; }

    public bool Recursive { get; set; } = true;
    public bool WriteLog { get; set; } = true;
    public bool FixLetters { get; set; }
    public bool FixNames { get; set; }
    public bool FromNames { get; set; }

    /// <summary>Replace albums that are set with the folder name, not only missing ones.</summary>
    public bool ReplaceAlbum { get; set; }

    /// <summary>Leave the files with OK status out of the list.</summary>
    public bool HideOk { get; set; }
    public TitleSplitMode SplitTitles { get; set; } = TitleSplitMode.Off;

    /// <summary>The saved settings, or the defaults if there are none or they can't be read.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new AppSettings();
    }

    /// <summary>Saves the settings. Failing to save only means they aren't remembered.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
