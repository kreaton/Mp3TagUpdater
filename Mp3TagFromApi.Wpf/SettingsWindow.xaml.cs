using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Mp3Tag.Albums.Lookup;
using Velopack;
using Velopack.Sources;

namespace Mp3TagFromApi.Wpf;

/// <summary>
/// The version, Check for updates, and the Spotify and Discogs keys. <see cref="Keys"/> holds the keys after Save.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// The public GitHub repository whose releases BuildRelease.ps1 uploads to. It is separate from Mp3TagUpdater's,
    /// so that "releases/latest" there keeps pointing at Mp3TagUpdater.
    /// </summary>
    public const string ReleasesUrl = "https://github.com/kreaton/Mp3TagFromApi";

    private readonly UpdateManager _updateManager = new(new GithubSource(ReleasesUrl, accessToken: null, prerelease: false));

    public SettingsWindow(ApiKeys keys)
    {
        InitializeComponent();

        // An .ico holds several sizes; take the largest so the icon stays sharp.
        var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/app.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        AppIcon.Source = decoder.Frames.MaxBy(f => f.PixelWidth);
        VersionText.Text = $"Version {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)}";

        Show(keys);
        Keys = keys;
    }

    private async void OnCheckForUpdates(object sender, RoutedEventArgs e)
    {
        const string caption = "Check for updates";
        if (!_updateManager.IsInstalled)
        {
            MessageBox.Show(this, "Updates are only available in the installed app.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        UpdateButton.IsEnabled = false;
        try
        {
            var newVersion = await _updateManager.CheckForUpdatesAsync();
            if (newVersion is null)
            {
                MessageBox.Show(this, "You have the latest version.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(this,
                $"Version {newVersion.TargetFullRelease.Version} is available. Download and install it now? Mp3TagFromApi restarts, and changes to the keys that aren't saved are lost.",
                caption, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            await _updateManager.DownloadUpdatesAsync(newVersion);
            _updateManager.ApplyUpdatesAndRestart(newVersion);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Update failed: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    public ApiKeys Keys { get; private set; }

    private void Show(ApiKeys keys)
    {
        SpotifyIdBox.Text = keys.SpotifyClientId ?? "";
        SpotifySecretBox.Password = keys.SpotifyClientSecret ?? "";
        DiscogsTokenBox.Password = keys.DiscogsToken ?? "";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Keys = new ApiKeys
        {
            SpotifyClientId = Value(SpotifyIdBox.Text),
            SpotifyClientSecret = Value(SpotifySecretBox.Password),
            DiscogsToken = Value(DiscogsTokenBox.Password),
        };
        DialogResult = true;

        static string? Value(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>
    /// Reads { "Spotify": { "ClientId", "ClientSecret" }, "Discogs": { "Token" } } and fills in the keys it has.
    /// Nothing is saved until Save.
    /// </summary>
    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import keys from appsettings.secret.json",
            Filter = "JSON settings (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using var doc = JsonDocument.Parse(File.ReadAllText(dialog.FileName), options);
            var found = 0;
            void Take(string section, string key, Action<string> set)
            {
                if (doc.RootElement.TryGetProperty(section, out var s) && s.ValueKind == JsonValueKind.Object
                    && s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                {
                    set(v.GetString()!.Trim());
                    found++;
                }
            }

            Take("Spotify", "ClientId", v => SpotifyIdBox.Text = v);
            Take("Spotify", "ClientSecret", v => SpotifySecretBox.Password = v);
            Take("Discogs", "Token", v => DiscogsTokenBox.Password = v);

            if (found == 0)
                MessageBox.Show(this, "The file has no Spotify or Discogs keys.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
