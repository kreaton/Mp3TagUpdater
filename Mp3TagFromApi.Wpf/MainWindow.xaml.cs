using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Win32;
using Mp3Tag.Albums;
using Mp3Tag.Albums.Lookup;
using Mp3Tag.Text;

namespace Mp3TagFromApi.Wpf;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private ApiKeys _keys;
    private readonly Dictionary<string, IAlbumSource> _sources = [];

    /// <summary>Waits until typing in the folder box pauses before reading the folder.</summary>
    private readonly DispatcherTimer _folderTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private CancellationTokenSource? _busy;
    private bool _closeWhenDone;

    /// <summary>The folder shown in the list, and its mp3 files as last read.</summary>
    private string? _folder;
    private IReadOnlyList<AudioFile> _files = [];

    /// <summary>Albums already loaded, by source and id, so switching between search results is quick.</summary>
    private readonly Dictionary<string, AlbumInfo> _albums = [];

    /// <summary>The chosen album, with remaster notes removed from its titles.</summary>
    private AlbumInfo? _album;
    private int _remasterNotes;

    private FolderPlan? _plan;
    private string? _planProblem;

    /// <summary>Values typed in Edit tags, by file path. They join every plan until Apply writes them.</summary>
    private readonly Dictionary<string, TagChange> _handEdits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Files written by the last Apply, shown as Updated.</summary>
    private HashSet<string> _updated = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastResult;

    private List<FileRow> _rows = [];
    private ListCollectionView? _view;

    public MainWindow()
    {
        InitializeComponent();
        _keys = _settings.GetKeys();
        _folderTimer.Tick += OnFolderTimer;

        DiscogsRadio.IsChecked = _settings.Source == "Discogs";
        SpotifyRadio.IsChecked = !DiscogsRadio.IsChecked;
        FixLettersBox.IsChecked = _settings.FixLetters;
        JsonBox.IsChecked = _settings.WriteJson;
        FolderBox.Text = _settings.Folder ?? "";
        UpdateButtons();
    }

    private string SourceName => DiscogsRadio.IsChecked == true ? "Discogs" : "Spotify";

    private bool FixLetters => FixLettersBox.IsChecked == true;

    private void SaveSettings()
    {
        _settings.Folder = _folder ?? _settings.Folder;
        _settings.Source = SourceName;
        _settings.FixLetters = FixLetters;
        _settings.WriteJson = JsonBox.IsChecked == true;
        _settings.Save();
    }

    // --- Choosing a folder -------------------------------------------------------

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder of mp3 files" };
        if (Directory.Exists(FolderBox.Text))
            dialog.InitialDirectory = FolderBox.Text;
        if (dialog.ShowDialog(this) == true)
            FolderBox.Text = dialog.FolderName;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = _busy is null && DroppedFolder(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_busy is null && DroppedFolder(e) is { } folder)
            FolderBox.Text = folder;
    }

    private static string? DroppedFolder(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths && Directory.Exists(paths[0]) ? paths[0] : null;

    private void OnFolderTextChanged(object sender, TextChangedEventArgs e)
    {
        _folderTimer.Stop();
        _folderTimer.Start();
    }

    private async void OnFolderTimer(object? sender, EventArgs e)
    {
        _folderTimer.Stop();
        var text = FolderBox.Text.Trim().Trim('"');
        if (text.Length == 0 || _busy is not null)
            return;
        if (!Directory.Exists(text))
        {
            StatusText.Text = $"There is no folder \"{text}\".";
            return;
        }

        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(text));
        if (!string.Equals(folder, _folder, StringComparison.OrdinalIgnoreCase))
            await LoadFolderAsync(folder);
    }

    private async void OnScan(object sender, RoutedEventArgs e)
    {
        if (_folder is not null)
            await LoadFolderAsync(_folder);
    }

    /// <summary>Reads the folder's mp3 files. A new folder starts over: its name becomes the search text.</summary>
    private async Task LoadFolderAsync(string folder)
    {
        await RunBusyAsync("Reading the mp3 files...", async cancellation =>
        {
            var files = await Task.Run(() => FolderReader.Read(folder, cancellation), cancellation);

            if (!string.Equals(folder, _folder, StringComparison.OrdinalIgnoreCase))
            {
                _updated.Clear();
                _handEdits.Clear();
                _lastResult = null;
                ClearAlbum();
                SearchBox.Text = TextRepair.FixOrKeep(new DirectoryInfo(folder).Name);
            }

            _folder = folder;
            _files = files;
            SaveSettings();
            Recompute();
            CheckHits(); // the same folder read again: its playing time may have changed
        });
    }

    // --- Finding the album -------------------------------------------------------

    private IAlbumSource Source
    {
        get
        {
            var name = SourceName;
            if (!_sources.TryGetValue(name, out var source))
            {
                source = name == "Discogs"
                    ? new DiscogsSource(_keys.DiscogsToken)
                    : new SpotifySource(_keys.SpotifyClientId, _keys.SpotifyClientSecret);
                _sources[name] = source;
            }
            return source;
        }
    }

    private void OnSourceChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        // Ids from one source mean nothing to the other.
        ClearAlbum();
        _lastResult = null;
        SaveSettings();
        Recompute();
    }

    private void ClearAlbum()
    {
        _album = null;
        _folderNameSettled = false;
        _hitCheck?.Cancel();
        FitSummaryText.Visibility = Visibility.Collapsed;
        HitBox.ItemsSource = null;
        AlbumText.Visibility = Visibility.Collapsed;
        MoreHitsPanel.Visibility = Visibility.Collapsed;
    }

    private async void OnSearch(object sender, RoutedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
            return;

        var source = Source;
        List<HitItem> items = [];
        var mayBeMore = false;
        var ok = await RunBusyAsync($"Searching {source.Name}...", async cancellation =>
        {
            IReadOnlyList<AlbumHit> hits;
            if (source.ParseId(query) is { } id)
            {
                // A pasted URL or id: load that album and list just it.
                var album = await GetAlbumAsync(source, id, cancellation);
                hits = [new AlbumHit(id, album.Artist, album.Title, album.Tracks.Count, album.Details)];
            }
            else
            {
                hits = await source.SearchAsync(query, cancellation);
                mayBeMore = hits.Count >= source.SearchLimit;
            }

            items = hits.Select(h => new HitItem(h)).ToList();
            if (items.Count == 0)
                StatusText.Text = $"{source.Name} found no albums for \"{query}\". Try fewer words, or the artist and album.";
        });

        ClearAlbum();
        if (!ok || items.Count == 0)
        {
            Recompute(keepStatus: true);
            return;
        }
        if (mayBeMore)
        {
            MoreHitsText.Text = $"{source.Name} returned {items.Count} albums, the most it gives for one search, so there may be more. "
                + "If the right album or edition isn't in the list, add words to the search (such as the year), or paste the album's URL.";
            MoreHitsPanel.Visibility = Visibility.Visible;
        }
        HitBox.ItemsSource = items;
        HitBox.SelectedIndex = 0; // loads the album
        UpdateButtons();
        CheckHits();
    }

    // --- How the search results fit the folder ------------------------------------

    /// <summary>Stops the check of the search results when they are replaced.</summary>
    private CancellationTokenSource? _hitCheck;

    private List<HitItem> HitItems => HitBox.ItemsSource as List<HitItem> ?? [];

    /// <summary>
    /// Marks each search result with how it fits the folder: first by the track count the search gives, then by
    /// loading the albums one at a time to compare their total playing time. It runs in the background, so a
    /// result can be picked meanwhile. Loaded albums are kept, so picking one afterwards is quick.
    /// </summary>
    private async void CheckHits()
    {
        _hitCheck?.Cancel();
        var items = HitItems;
        if (items.Count == 0 || _files.Count == 0)
        {
            FitSummaryText.Visibility = Visibility.Collapsed;
            return;
        }

        // Not disposed: a newer search or folder may still cancel it after this check has finished.
        var check = _hitCheck = new CancellationTokenSource();
        var source = Source;
        foreach (var item in items)
        {
            if (_albums.TryGetValue($"{source.Name}:{item.Hit.Id}", out var known))
                item.SetFit(AlbumFit.Of(known, _files));
            else if (item.Hit.TrackCount is { } n && n != _files.Count)
                item.SetFit(AlbumFit.FromTrackCount(n, _files)); // can't be used, so there's no need to load it
            else
                item.SetFit(item.Hit.TrackCount is { } count ? AlbumFit.FromTrackCount(count, _files) : null, checking: true);
        }
        ShowFitSummary(items);

        string? problem = null;
        foreach (var item in items.Where(i => i.Checking))
        {
            try
            {
                var album = await GetAlbumAsync(source, item.Hit.Id, check.Token);
                if (check.IsCancellationRequested)
                    return;
                item.SetFit(AlbumFit.Of(album, _files));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is LookupException or HttpRequestException)
            {
                // Such as Discogs' limit on requests per minute: leave the rest unchecked.
                problem = ex.Message;
                break;
            }
            ShowFitSummary(items);
        }

        if (check.IsCancellationRequested)
            return;
        foreach (var item in items.Where(i => i.Checking))
            item.SetFit(item.Fit);
        ShowFitSummary(items, problem);
    }

    /// <summary>"3 of 10 albums have 12 tracks and about the same playing time as the folder (41:23)."</summary>
    private void ShowFitSummary(List<HitItem> items, string? problem = null)
    {
        var tracks = _files.Count;
        var length = Durations.Format(AlbumFit.PlayingTime(_files));
        var match = items.Count(i => i.Level == FitLevel.Match);
        var sameCount = items.Count(i => i.Level is FitLevel.TracksMatch or FitLevel.LengthDiffers);
        var checking = items.Count(i => i.Checking);

        var text = items.Count == 1
            ? items[0] switch
            {
                { Checking: true } => $"Comparing the album with the folder ({Count(tracks, "track")}, {length})...",
                { Level: FitLevel.Match } => $"The album has {Count(tracks, "track")} and about the same playing time as the folder ({length}).",
                { Level: FitLevel.LengthDiffers, Fit: { } f } => $"The album has {Count(tracks, "track")} like the folder, but plays {Durations.Format(f.AlbumLength)} against the folder's {length}: probably another edition.",
                { Level: FitLevel.TracksMatch } => $"The album has {Count(tracks, "track")} like the folder. The source doesn't list every track's length, so the playing time can't be compared.",
                _ => $"The album doesn't have {Count(tracks, "track")} like the folder, so it can't be used for it.",
            }
            : $"{match} of {items.Count} albums {(match == 1 ? "has" : "have")} {Count(tracks, "track")} and about the same playing time as the folder ({length})"
                + (sameCount > 0 ? $"; {sameCount} more {(sameCount == 1 ? "has" : "have")} {tracks} tracks but another or no listed playing time" : "")
                + (checking > 0 ? $". Checking {checking} more..." : ".");
        if (problem is not null)
            text += $" Not all could be checked: {problem}";

        FitSummaryText.Text = text;
        FitSummaryText.Visibility = Visibility.Visible;
    }

    private async void OnHitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HitBox.SelectedItem is not HitItem item || _busy is not null)
            return;

        var source = Source;
        await RunBusyAsync($"Loading the album from {source.Name}...", async cancellation =>
        {
            var raw = await GetAlbumAsync(source, item.Hit.Id, cancellation);
            _album = raw.WithoutRemasterNotes();
            _folderNameSettled = false;
            _remasterNotes = _album.TitlesChangedFrom(raw);
            _lastResult = null;
            ShowAlbum(_album);
            Recompute();
        });
    }

    private async Task<AlbumInfo> GetAlbumAsync(IAlbumSource source, string id, CancellationToken cancellation)
    {
        var key = $"{source.Name}:{id}";
        if (!_albums.TryGetValue(key, out var album))
            _albums[key] = album = await source.GetAlbumAsync(id, cancellation);
        return album;
    }

    private void ShowAlbum(AlbumInfo album)
    {
        var notes = _remasterNotes > 0 ? $" Remaster notes such as \"- 2019 Remaster\" were removed from {_remasterNotes} {(_remasterNotes == 1 ? "title" : "titles")}." : "";
        AlbumDetailsRun.Text = $"{album.Artist} – {album.Title}: {album.Details}.{notes} ";
        AlbumLinkRun.Text = $"Open on {album.Source}";
        AlbumLink.NavigateUri = Uri.TryCreate(album.Url, UriKind.Absolute, out var uri) ? uri : null;
        // Discogs asks apps that show its data to say so.
        AttributionRun.Text = album.Source == "Discogs" ? "  Data provided by Discogs." : "";
        AlbumText.Visibility = Visibility.Visible;
    }

    private void OnAlbumLink(object sender, RequestNavigateEventArgs e)
    {
        Open(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_keys) { Owner = this };
        if (window.ShowDialog() != true)
            return;

        _keys = window.Keys;
        _settings.SetKeys(_keys);
        SaveSettings();
        foreach (var source in _sources.Values)
            source.Dispose();
        _sources.Clear();
    }

    // --- Planning ----------------------------------------------------------------

    private void OnFixLettersChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        SaveSettings();
        Recompute();
    }

    /// <summary>Works out the changes for the files and album as they are now, and shows them.</summary>
    private void Recompute(bool keepStatus = false)
    {
        _plan = null;
        _planProblem = null;

        if (_folder is not null && _files.Count > 0)
        {
            if (_album is not null && _album.Tracks.Count != _files.Count)
                _planProblem = $"The album has {_album.Tracks.Count} tracks, but the folder has {_files.Count} mp3 files. Choose another album or edition.";
            _plan = MakePlan(_folder, _planProblem is null ? _album : null, _files, FixLetters, _handEdits);
        }

        ShowRows();
        if (!keepStatus)
            ShowStatus();
        UpdateButtons();
    }

    /// <summary>
    /// The album's tags, or else the letter fixes, with the hand edits on top. Null when there is nothing to do.
    /// Also used by Apply on the files as they are then.
    /// </summary>
    private static FolderPlan? MakePlan(string folder, AlbumInfo? album, IReadOnlyList<AudioFile> files, bool fixLetters,
        IReadOnlyDictionary<string, TagChange> handEdits)
    {
        var plan = album is not null ? AlbumTagger.Plan(folder, album, files, fixLetters)
            : fixLetters ? AlbumTagger.PlanLetterFixes(folder, files)
            : handEdits.Count > 0 ? AlbumTagger.PlanNothing(folder, files)
            : null;
        return plan is null ? null : AlbumTagger.WithHandEdits(plan, handEdits);
    }

    private void ShowRows()
    {
        var plans = _plan?.Files.ToDictionary(f => f.File.Path, StringComparer.OrdinalIgnoreCase) ?? [];
        // In album order once matched, otherwise in file-name order.
        var files = _plan?.Album is not null ? _plan.Files.Select(f => f.File) : _files;
        _rows = files.Select(f => new FileRow(f, plans.GetValueOrDefault(f.Path), _updated.Contains(f.Path))).ToList();
        _view = new ListCollectionView(_rows) { Filter = Filter };
        FileGrid.ItemsSource = _view;

        FilterAll.Content = $"All files ({_rows.Count})";
        FilterChanges.Content = $"Changes ({_rows.Count(r => r.HasChanges)})";
        FilterProblems.Content = $"Errors ({_rows.Count(r => r.IsProblem)})";
        UpdateCount();
    }

    private void ShowStatus()
    {
        string text;
        if (_folder is null)
            text = "Choose a folder of mp3 files, then search Spotify or Discogs for its album. Nothing is changed until you apply.";
        else if (_files.Count == 0)
            text = "There are no .mp3 files in this folder. Subfolders are not read.";
        else if (_plan is null)
            text = _planProblem ?? $"{Count(_files.Count, "mp3 file")}. Search {SourceName} for the album, tick Fix mojibake to only correct garbled letters, or press F2 to edit a file's tags.";
        else
        {
            var parts = new List<string>();
            if (_planProblem is not null)
                parts.Add(_planProblem);
            var changed = _plan.Changed.Count;
            var byHand = _plan.Files.Count(f => f.EditedByHand);
            if (_plan.Album is not null)
            {
                parts.Add(_plan.Method == MatchMethod.TrackName
                    ? "Every file was matched to its track by title."
                    : $"The titles identified {_plan.NameMatches} of {_files.Count} files, so the files get tracks 1–{_files.Count} in file-name order. Check the list.");
                parts.Add(changed == 0 ? "The files already match the album." : $"{Count(changed, "file")} will change.");
                if (changed == 0 && !_folderNameSettled && SuggestedFolderName is { } name)
                    parts.Add($"Rename folder gives the folder the album's name, \"{name}\".");
            }
            else
            {
                if (FixLetters)
                    parts.Add(_plan.Files.Any(f => f.HasChanges && !f.EditedByHand) ? "Garbled letters will be fixed." : "No garbled letters found.");
                parts.Add(changed == 0 ? "Nothing will change." : $"{Count(changed, "file")} will change.");
                if (_planProblem is null)
                    parts.Add($"Search {SourceName} to tag the files as an album.");
            }
            if (byHand > 0)
                parts.Add($"{Count(byHand, "file")} edited by hand.");

            if (_plan.Unreadable.Count is > 0 and var unreadable)
                parts.Add($"{Count(unreadable, "file")} can't be read. Repair {(unreadable == 1 ? "it" : "them")} with Mp3TagUpdater first.");
            text = string.Join(" ", parts);
        }

        StatusText.Text = _lastResult is null ? text : $"{_lastResult} {text}";
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    /// <summary>The album's name for the folder, when it differs from the folder's name now.</summary>
    private string? SuggestedFolderName => _plan is { Album: { } album } plan ? ApplyWindow.SuggestedName(plan.Folder, album) : null;

    /// <summary>
    /// The folder's name was settled in an Apply for this folder and album: renamed as the user wanted, or kept.
    /// The album's name is then only offered again, unticked, along with other changes.
    /// </summary>
    private bool _folderNameSettled;

    /// <summary>There are changes to write, or, with an album, the folder can get the album's name.</summary>
    private bool CanApply =>
        _plan is { } plan && (plan.Changed.Count > 0 || SuggestedFolderName is not null && !_folderNameSettled)
        && (plan.Album is null || plan.Unreadable.Count == 0); // a missing file would leave a gap in the album

    private void UpdateButtons()
    {
        var idle = _busy is null;
        ApplyButton.IsEnabled = idle && CanApply;
        var changes = _plan?.Changed.Count ?? 0;
        ApplyButton.Content = !CanApply ? "Apply changes..." : changes > 0 ? $"Apply {Count(changes, "change")}..." : "Rename folder...";
        CancelButton.IsEnabled = !idle;
        SearchButton.IsEnabled = idle;
        HitBox.IsEnabled = idle && HitBox.Items.Count > 0;
        FolderBox.IsEnabled = SearchBox.IsEnabled = SpotifyRadio.IsEnabled = DiscogsRadio.IsEnabled = SettingsButton.IsEnabled
            = FixLettersBox.IsEnabled = JsonBox.IsEnabled = idle;
        ScanButton.IsEnabled = idle && _folder is not null;
    }

    // --- Applying ----------------------------------------------------------------

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (_plan is not { } plan || _folder is not { } folder || !CanApply)
            return;

        var changed = plan.Changed;
        var lines = new List<string>();
        if (plan.Album is { } shownAlbum)
        {
            lines.Add(changed.Count == 0
                ? $"The files already match \"{shownAlbum.AlbumArtist} – {shownAlbum.Title}\" from {shownAlbum.Source}."
                : $"{Count(changed.Count, "file")} tagged as \"{shownAlbum.AlbumArtist} – {shownAlbum.Title}\" from {shownAlbum.Source}.");
            if (plan.Method == MatchMethod.FileOrder && changed.Count > 0)
                lines.Add("The files were matched to tracks by file order, not by title. Check the Title column first.");
        }
        else
        {
            lines.Add($"{Count(changed.Count, "file")} changed.");
        }
        if (changed.Count(f => f.EditedByHand) is > 0 and var byHand)
            lines.Add($"{Count(byHand, "file")} with tags edited by hand.");
        if (changed.Count(f => f.Renames) is > 0 and var renames)
            lines.Add($"{Count(renames, "file")} renamed.");
        var writeJson = plan.Album is not null && JsonBox.IsChecked == true;
        if (writeJson)
            lines.Add($"The album saved as \"{AlbumJson.FileNameFor(plan.Album!)}\".");

        var risky = changed.Count > 0 && plan.Method == MatchMethod.FileOrder;
        var suggested = SuggestedFolderName;
        var dialog = new ApplyWindow(folder, lines, risky, suggested, renameTicked: !_folderNameSettled) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        var renameTo = dialog.RenameTo;
        if (suggested is not null)
            _folderNameSettled = true;
        if (changed.Count == 0 && renameTo is null)
            return; // only the rename was offered, and it was turned down

        var album = plan.Album;
        var fixLetters = FixLetters;
        var handEdits = new Dictionary<string, TagChange>(_handEdits, StringComparer.OrdinalIgnoreCase);
        var shown = Fingerprint(plan);
        Bar.Maximum = Math.Max(1, changed.Count);
        var progress = new Progress<int>(n => { Bar.IsIndeterminate = false; Bar.Value = n; });
        var current = folder; // the folder's path, which the rename at the end changes

        await RunBusyAsync("Writing tags and names...", async cancellation =>
        {
            try
            {
                var (written, renamed, json, renameError) = await Task.Run(() =>
                {
                    // Work from the files as they are now, and only go ahead if that is still what was shown.
                    var files = FolderReader.Read(folder, cancellation);
                    var fresh = MakePlan(folder, album, files, fixLetters, handEdits);
                    if (fresh is null || Fingerprint(fresh) != shown)
                        throw new InvalidOperationException("The files have changed since they were listed, so nothing was written. Check the list again.");

                    AlbumTagger.Apply(fresh, progress, cancellation);
                    // Written now, and the files may have new names, so the edits have done their job.
                    Dispatcher.Invoke(_handEdits.Clear);
                    var json = writeJson ? AlbumJson.Write(fresh) : null;

                    // Last, once everything in the folder is written. If it fails, the tags are still done.
                    string? renameError = null;
                    if (renameTo is not null)
                    {
                        try
                        {
                            current = AlbumTagger.RenameFolder(folder, renameTo);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            renameError = ex.Message;
                        }
                    }

                    _updated = fresh.Changed.Select(f => Path.Combine(current, f.NewFileName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    return (fresh.Changed.Count, fresh.Changed.Count(f => f.Renames), json, renameError);
                }, cancellation);

                var folderNote = renameTo is null ? ""
                    : renameError is null ? $" The folder is now \"{Path.GetFileName(current)}\"."
                    : " The folder couldn't be renamed.";
                _lastResult = $"Done: {Count(written, "file")} updated, {renamed} renamed{(json is null ? "" : $", saved {Path.GetFileName(json)}")}.{folderNote}";
                if (renameError is not null)
                    MessageBox.Show(this, $"The tags were written, but the folder couldn't be renamed to \"{renameTo}\":\n\n{renameError}\n\nA program may be using one of its files. Close it and apply again, or rename the folder in Explorer.",
                        Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (current != folder)
                {
                    // Set _folder first, so the folder box doesn't take the new path for another folder and start over.
                    _folder = current;
                    FolderBox.Text = current;
                    SaveSettings();
                }
                // Show the folder as it is now, also after an error or a cancel half-way.
                _files = await Task.Run(() => FolderReader.Read(current));
                Recompute();
            }
        });
    }

    /// <summary>What a plan would do, to tell whether the files changed between listing and applying.</summary>
    private static string Fingerprint(FolderPlan plan) =>
        string.Join("\n", plan.Files.Select(f => $"{f.File.Path}|{string.Join("|", f.Changes)}"));

    private void OnCancel(object sender, RoutedEventArgs e) => RequestCancel();

    private void RequestCancel()
    {
        _busy?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "Cancelling...";
    }

    /// <summary>Lets a running job stop at the next file, then closes.</summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_busy is null)
        {
            SaveSettings();
            foreach (var source in _sources.Values)
                source.Dispose();
            return;
        }
        e.Cancel = true;
        _closeWhenDone = true;
        RequestCancel();
    }

    /// <summary>
    /// Runs one job at a time with the controls locked, and shows its errors in the status line.
    /// Returns false when it failed or was cancelled.
    /// </summary>
    private async Task<bool> RunBusyAsync(string message, Func<CancellationToken, Task> work)
    {
        if (_busy is not null)
            return false;

        _busy = new CancellationTokenSource();
        UpdateButtons();
        Bar.Value = 0;
        Bar.IsIndeterminate = true;
        StatusText.Text = message;
        var ok = false;
        try
        {
            await work(_busy.Token);
            ok = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled.";
        }
        catch (LookupException ex)
        {
            StatusText.Text = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            StatusText.Text = $"Couldn't reach the service: {ex.Message}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TagLib.CorruptFileException)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Bar.IsIndeterminate = false;
            Bar.Value = 0;
            _busy.Dispose();
            _busy = null;
            UpdateButtons();
        }

        if (_closeWhenDone)
            Close();
        return ok;
    }

    // --- Filtering ---------------------------------------------------------------

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        _view?.Refresh();
        UpdateCount();
    }

    private bool Filter(object item)
    {
        var row = (FileRow)item;
        var filter = FilterBox.SelectedItem;
        var show = filter == FilterChanges ? row.HasChanges
            : filter == FilterProblems ? row.IsProblem
            : true;
        var find = FindBox.Text.Trim();
        return show && (find.Length == 0 || row.Matches(find));
    }

    private void UpdateCount()
    {
        if (CountText is null)
            return;
        CountText.Text = _view is null || _rows.Count == 0 ? "" : $"Showing {_view.Count} of {_rows.Count}";
    }

    // --- Editing tags by hand ----------------------------------------------------

    /// <summary>The selected files whose tags can be edited: not ones that can't be read, and nothing while busy.</summary>
    private List<FileRow> EditableRows() => _busy is null ? SelectedRows.Where(r => r.File.Error is null).ToList() : [];

    private void OnGridContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        EditTagsItem.IsEnabled = EditableRows().Count > 0;
        UndoEditsItem.IsEnabled = _busy is null && SelectedRows.Any(r => _handEdits.ContainsKey(r.FilePath));
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            OnEditTags(sender, e);
        }
    }

    /// <summary>
    /// Opens Edit tags on what Apply would write, and adds the changed fields to the plan. Nothing is written
    /// until Apply, as with the album's values.
    /// </summary>
    private void OnEditTags(object sender, RoutedEventArgs e)
    {
        var rows = EditableRows();
        if (rows.Count == 0)
            return;

        var dialog = new TagEditWindow(rows.Select(r => r.Values).ToList()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Edit is not { ChangesTags: true } edit)
            return;

        foreach (var row in rows)
            _handEdits[row.FilePath] = _handEdits.TryGetValue(row.FilePath, out var earlier) ? earlier.Then(edit) : edit;
        RecomputeKeepingSelection(rows);
    }

    private void OnUndoHandEdits(object sender, RoutedEventArgs e)
    {
        var rows = SelectedRows.ToList();
        if (_busy is not null || !rows.Any(r => _handEdits.Remove(r.FilePath)))
            return;
        RecomputeKeepingSelection(rows);
    }

    private void RecomputeKeepingSelection(IReadOnlyList<FileRow> rows)
    {
        _lastResult = null;
        Recompute();
        var paths = rows.Select(r => r.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        FileGrid.SelectedItems.Clear();
        foreach (var row in _rows.Where(r => paths.Contains(r.FilePath)))
            FileGrid.SelectedItems.Add(row);
        if (FileGrid.SelectedItem is { } first)
            FileGrid.ScrollIntoView(first);
        FileGrid.Focus();
    }

    // --- Files -------------------------------------------------------------------

    private IEnumerable<FileRow> SelectedRows => FileGrid.SelectedItems.OfType<FileRow>();

    /// <summary>Opens the file in its default program, as Explorer does: an mp3 plays in the default player.</summary>
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowAt(e.OriginalSource) is { } row)
            OpenFile(row.FilePath);
    }

    private void OnOpenSelected(object sender, RoutedEventArgs e)
    {
        if (SelectedRows.FirstOrDefault() is { } row)
            OpenFile(row.FilePath);
    }

    private void OpenFile(string path)
    {
        if (File.Exists(path))
            Open(path);
        else
            MessageBox.Show(this, $"It is no longer there:\n\n{path}\n\nChoose Scan to read the folder again.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static FileRow? RowAt(object source)
    {
        var element = source as DependencyObject;
        while (element is not null and not DataGridRow)
            element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        return (element as DataGridRow)?.Item as FileRow;
    }

    private void OnShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (SelectedRows.FirstOrDefault() is { } row && File.Exists(row.FilePath))
            Process.Start("explorer.exe", $"/select,\"{row.FilePath}\"");
    }

    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        var paths = SelectedRows.Select(r => r.FilePath).ToList();
        if (paths.Count > 0)
            Clipboard.SetText(string.Join(Environment.NewLine, paths));
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (_folder is not null && Directory.Exists(_folder))
            Open(_folder);
    }

    private void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
