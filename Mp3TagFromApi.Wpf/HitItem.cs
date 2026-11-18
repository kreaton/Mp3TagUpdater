using System.ComponentModel;
using Mp3Tag.Albums;
using Mp3Tag.Albums.Lookup;

namespace Mp3TagFromApi.Wpf;

/// <summary>A search result as listed in the Album box, with how well it fits the folder once that is known.</summary>
public sealed class HitItem(AlbumHit hit) : INotifyPropertyChanged
{
    public AlbumHit Hit { get; } = hit;

    public string Details => $"({Hit.Details})";

    public AlbumFit? Fit { get; private set; }

    /// <summary>Being loaded to compare its playing time with the folder's.</summary>
    public bool Checking { get; private set; }

    public FitLevel Level => Fit?.Level ?? FitLevel.Unknown;

    /// <summary>"✓ 12 tracks, 41:30 like the folder", "12 tracks, but 45:10 (folder 41:23)", "9 tracks (folder 12)".</summary>
    public string FitText => Fit switch
    {
        null => Checking ? "checking..." : "",
        { Level: FitLevel.TracksDiffer } f => $"{f.Tracks} tracks (folder {f.Files})",
        { Level: FitLevel.TracksMatch } f => Checking ? $"✓ {f.Tracks} tracks, checking the length..." : $"✓ {f.Tracks} tracks, length not listed",
        { Level: FitLevel.LengthDiffers } f => $"{f.Tracks} tracks, but {Durations.Format(f.AlbumLength)} (folder {Durations.Format(f.FolderLength)})",
        { } f => $"✓ {f.Tracks} tracks, {Durations.Format(f.AlbumLength)} like the folder",
    };

    /// <summary>Picks the colour: Success, Caution, or Neutral.</summary>
    public string Tone => Level switch
    {
        FitLevel.Match => "Success",
        FitLevel.LengthDiffers => "Caution",
        _ => "Neutral",
    };

    /// <summary>Albums with another number of tracks can't be used, so they are dimmed.</summary>
    public double Opacity => Level == FitLevel.TracksDiffer ? 0.6 : 1;

    public void SetFit(AlbumFit? fit, bool checking = false)
    {
        Fit = fit;
        Checking = checking;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); // every property may change
    }

    /// <summary>What screen readers and keyboard search in the box see.</summary>
    public override string ToString() => $"{Hit.Artist} – {Hit.Title} {Details} {FitText}";

    public event PropertyChangedEventHandler? PropertyChanged;
}
