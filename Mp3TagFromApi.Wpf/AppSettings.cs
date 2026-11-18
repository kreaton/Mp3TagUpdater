using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mp3Tag.Albums.Lookup;

namespace Mp3TagFromApi.Wpf;

/// <summary>
/// The choices remembered between runs, kept in %APPDATA%\Mp3TagFromApi\settings.json.
/// The API keys are encrypted with Windows DPAPI, so only this Windows user can read them.
/// </summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mp3TagFromApi", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Ties the encrypted keys to this app, so another program running as the user can't decrypt them by accident.
    private static readonly byte[] Entropy = "Mp3TagFromApi.ApiKeys"u8.ToArray();

    /// <summary>The last folder.</summary>
    public string? Folder { get; set; }

    /// <summary>"Spotify" or "Discogs".</summary>
    public string Source { get; set; } = "Spotify";

    public bool FixLetters { get; set; } = true;
    public bool WriteJson { get; set; } = true;

    /// <summary>The API keys as JSON, encrypted with DPAPI for the current user, in base 64.</summary>
    public string? ProtectedKeys { get; set; }

    public ApiKeys GetKeys()
    {
        if (string.IsNullOrEmpty(ProtectedKeys))
            return new ApiKeys();
        try
        {
            var json = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedKeys), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<ApiKeys>(json) ?? new ApiKeys();
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return new ApiKeys(); // saved by another Windows user or damaged: enter the keys again
        }
    }

    public void SetKeys(ApiKeys keys)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(keys);
        ProtectedKeys = Convert.ToBase64String(ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
    }

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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
