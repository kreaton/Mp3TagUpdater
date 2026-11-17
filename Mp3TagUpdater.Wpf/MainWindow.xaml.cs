using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Mp3TagUpdater.Models;
using Mp3TagUpdater.Services;

namespace Mp3TagUpdater.Wpf;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private volatile Mp3Progress? _latest;
    private CancellationTokenSource? _cancellation;
    private bool _closeWhenDone;

    private List<FileRow> _rows = [];
    private ListCollectionView? _view;

    /// <summary>The settings of the last completed scan, which Apply repeats with writing switched on.</summary>
    private Mp3RunOptions? _scanned;
    private string? _logPath;

    private readonly AppSettings _settings = AppSettings.Load();

    public MainWindow()
    {
        InitializeComponent();
        _refresh.Tick += (_, _) => ShowProgress();

        FolderBox.Text = _settings.Folder ?? "";
        RecursiveBox.IsChecked = _settings.Recursive;
        LogBox.IsChecked = _settings.WriteLog;
        FixLettersBox.IsChecked = _settings.FixLetters;
        FixNamesBox.IsChecked = _settings.FixNames;
        FromNamesBox.IsChecked = _settings.FromNames;
        SplitBox.SelectedIndex = (int)_settings.SplitTitles;
        HideOkBox.IsChecked = _settings.HideOk;
        UpdateButtons();
    }

    /// <summary>Remembers the folder and options for the next start.</summary>
    private void SaveSettings()
    {
        _settings.Folder = FolderBox.Text.Trim() is { Length: > 0 } folder ? folder : _settings.Folder;
        _settings.Recursive = RecursiveBox.IsChecked == true;
        _settings.WriteLog = LogBox.IsChecked == true;
        _settings.FixLetters = FixLettersBox.IsChecked == true;
        _settings.FixNames = FixNamesBox.IsChecked == true;
        _settings.FromNames = FromNamesBox.IsChecked == true;
        _settings.SplitTitles = SplitMode;
        _settings.HideOk = HideOkBox.IsChecked == true;
        _settings.Save();
    }

    private TitleSplitMode SplitMode => SplitBox.SelectedIndex is >= 0 and <= 3 ? (TitleSplitMode)SplitBox.SelectedIndex : TitleSplitMode.Off;

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
        e.Effects = _cancellation is null && DroppedFolder(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_cancellation is null && DroppedFolder(e) is { } folder)
            FolderBox.Text = folder;
    }

    private static string? DroppedFolder(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths && Directory.Exists(paths[0]) ? paths[0] : null;

    private void OnOptionChanged(object sender, RoutedEventArgs e) => UpdateButtons();

    private void OnSplitChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private Mp3RunOptions CurrentOptions(bool dryRun) =>
        new(Path.TrimEndingDirectorySeparator(FolderBox.Text.Trim()))
        {
            DryRun = dryRun,
            Recursive = RecursiveBox.IsChecked == true,
            FixLetters = FixLettersBox.IsChecked == true,
            FixNames = FixNamesBox.IsChecked == true,
            FillFromFileName = FromNamesBox.IsChecked == true,
            SplitTitles = SplitMode
        };

    private void UpdateButtons()
    {
        // Checked handlers fire while the XAML is still loading.
        if (ScanButton is null || FolderBox is null || RecursiveBox is null || FixLettersBox is null || FixNamesBox is null || FromNamesBox is null || SplitBox is null)
            return;

        var idle = _cancellation is null;
        ScanButton.IsEnabled = idle && !string.IsNullOrWhiteSpace(FolderBox.Text);

        // Apply only writes what the last scan showed, so the folder and options must still match it.
        // Every change a scan found is still to be applied, including a rename of a damaged mp3,
        // which keeps its Error or Warning status.
        var pending = _rows.Count(r => r.HasChanges);
        var scanStillValid = _scanned is not null && _scanned == CurrentOptions(dryRun: true);
        ApplyButton.IsEnabled = idle && scanStillValid && pending > 0;
        ApplyButton.Content = scanStillValid && pending > 0 ? $"Apply {pending} {(pending == 1 ? "change" : "changes")}..." : "Apply changes...";

        CancelButton.IsEnabled = !idle;
        // Installing an update restarts the app, so it must not be reachable during a run.
        AboutButton.IsEnabled = idle;
        FolderBox.IsEnabled = RecursiveBox.IsEnabled = LogBox.IsEnabled = FixLettersBox.IsEnabled = FixNamesBox.IsEnabled = FromNamesBox.IsEnabled = SplitBox.IsEnabled = idle;
    }

    // --- Running -----------------------------------------------------------------

    private async void OnScan(object sender, RoutedEventArgs e) => await RunAsync(CurrentOptions(dryRun: true));

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        var options = CurrentOptions(dryRun: false);
        var changed = _rows.Where(r => r.HasChanges).ToList();
        var mp3s = changed.Where(r => r.Record.Kind == Mp3EntryKind.Mp3).ToList();
        var otherFiles = changed.Count(r => r.Record.Kind == Mp3EntryKind.File);
        var folders = changed.Count(r => r.Record.Kind == Mp3EntryKind.Folder);
        var repairs = mp3s.Count(r => r.Record.Status == Mp3Status.Repairable);
        // Record.Edit rather than the row's Edit, which leaves out a damaged mp3 that is only renamed.
        var edits = mp3s.Select(r => r.Record.Edit).OfType<Mp3TagEdit>().ToList();
        var letters = edits.Count(e => e.FixesLetters);
        var splits = edits.Count(e => e.SplitsTitle);
        var albums = edits.Count(e => e.FillsAlbum);
        var fromNames = edits.Count(e => e.FillsFromFileName);
        var renames = edits.Count(e => e.FileName is not null);

        var lines = new List<string>();
        if (repairs > 0)
            lines.Add($"•  {Files(repairs)} repaired. Each original is kept next to it as .bak.");
        if (letters > 0)
            lines.Add($"•  {Files(letters)} given corrected letters (æ, ø, å and others).");
        if (splits > 0)
            lines.Add($"•  {Files(splits)} given the artist from the title. Check the Artist and Title columns first.");
        if (fromNames > 0)
            lines.Add($"•  {Files(fromNames)} given missing tags (title, artist, track, disc) from the file name.");
        if (albums > 0)
            lines.Add($"•  {Files(albums)} given an Album tag from the folder name.");
        if (renames > 0)
            lines.Add($"•  {Files(renames)} renamed.");
        if (otherFiles > 0)
            lines.Add($"•  {Count(otherFiles, "other file is", "other files are")} renamed, such as covers and playlists.");
        if (folders > 0)
            lines.Add($"•  {Count(folders, "folder is", "folders are")} renamed.");

        var what = string.Join(" and ", new[]
        {
            mp3s.Count > 0 ? $"changes {Count(mp3s.Count, "mp3 file", "mp3 files")}" : null,
            otherFiles + folders > 0 ? $"renames {Count(otherFiles + folders, "other file or folder", "other files and folders")}" : null
        }.OfType<string>());

        var answer = MessageBox.Show(
            this,
            $"This {what} in\n\n{options.Directory}\n\n{string.Join("\n", lines)}\n\n" +
            "Modified dates are kept. The folder is read again first, so the changes are worked out from the files as they are now.\n\nApply the changes?",
            "Apply changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes)
            await RunAsync(options);

        static string Files(int n) => n == 1 ? "1 file is" : $"{n} files are";
        static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
    }

    private async Task RunAsync(Mp3RunOptions options)
    {
        if (!Directory.Exists(options.Directory))
        {
            MessageBox.Show(this, $"The folder doesn't exist:\n\n{options.Directory}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SaveSettings();
        var writeLog = LogBox.IsChecked == true;
        _latest = null;
        _logPath = null;
        _scanned = null;
        Bar.Value = 0;
        Bar.IsIndeterminate = true;
        OpenLogButton.Visibility = OpenFolderButton.Visibility = Visibility.Collapsed;
        _cancellation = new CancellationTokenSource();
        UpdateButtons();
        StatusText.Text = options.DryRun ? "Scanning..." : "Applying changes...";
        _refresh.Start();

        var token = _cancellation.Token;
        try
        {
            var (result, logPath, logError) = await Task.Run(() =>
            {
                var result = new Mp3Library().Run(options, new DirectProgress<Mp3Progress>(p => _latest = p), token);

                // A dry run writes a log too, as the record of what was found.
                string? logPath = null, logError = null;
                if (writeLog && result.Records.Count > 0)
                {
                    try
                    {
                        logPath = Mp3CsvLog.Write(result.Records, options.Directory);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logError = ex.Message;
                    }
                }
                return (result, logPath, logError);
            });

            ShowResult(options, result);
            _logPath = logPath;
            OpenLogButton.Visibility = logPath is null ? Visibility.Collapsed : Visibility.Visible;
            OpenFolderButton.Visibility = Visibility.Visible;
            if (options.DryRun && !result.Cancelled)
                _scanned = options;

            if (logError is not null)
                MessageBox.Show(this, $"The CSV log couldn't be written:\n\n{logError}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _refresh.Stop();
            Bar.IsIndeterminate = false;
            _cancellation.Dispose();
            _cancellation = null;
            UpdateButtons();
        }

        if (_closeWhenDone)
            Close();
    }

    private void ShowResult(Mp3RunOptions options, Mp3RunResult result)
    {
        _rows = result.Records
            .Select(r => new FileRow(r, options.Directory))
            .OrderBy(r => r.RelativePath, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _view = new ListCollectionView(_rows) { Filter = Filter };
        FileGrid.ItemsSource = _view;

        FilterAll.Content = $"All files ({_rows.Count})";
        FilterChanges.Content = $"Changes ({_rows.Count(r => r.HasChanges)})";
        FilterUpdatable.Content = $"Updatable ({_rows.Count(r => r.Status == Mp3Status.Updatable)})";
        FilterProblems.Content = $"Errors and warnings ({_rows.Count(r => r.IsProblem)})";
        UpdateCount();

        Bar.Maximum = 1;
        Bar.Value = result.Cancelled ? 0 : 1;

        var prefix = (options.DryRun, result.Cancelled) switch
        {
            (true, false) => "Scan",
            (true, true) => "Scan cancelled",
            (false, false) => "Done",
            (false, true) => "Cancelled"
        };

        if (result.Records.Count == 0)
            StatusText.Text = result.Cancelled ? "Cancelled." : "No .mp3 files found.";
        else if (!options.DryRun)
            StatusText.Text = $"{prefix}: {result.Summary}.";
        else if (result.HasChanges)
            StatusText.Text = $"{prefix}: {result.Summary}. Nothing was changed. Choose Apply to write the changes.";
        else
            StatusText.Text = $"{prefix}: {result.Summary}. Nothing needs changing.";
    }

    private void OnCancel(object sender, RoutedEventArgs e) => RequestCancel();

    private void RequestCancel()
    {
        _cancellation?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "Cancelling after the current file...";
    }

    /// <summary>Lets a running job finish its current file, then closes.</summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_cancellation is null)
        {
            SaveSettings();
            return;
        }
        e.Cancel = true;
        _closeWhenDone = true;
        RequestCancel();
    }

    /// <summary>
    /// Progress is reported on the worker thread. The UI picks it up on a timer, because
    /// reading tags can report far faster than it could usefully redraw.
    /// </summary>
    private void ShowProgress()
    {
        if (_latest is not { } p || _cancellation?.IsCancellationRequested != false)
            return;

        Bar.IsIndeterminate = p.Total == 0;
        Bar.Maximum = Math.Max(p.Total, 1);
        Bar.Value = p.Processed;
        StatusText.Text = p.Total == 0 ? $"{p.Phase}..." : $"{p.Phase}: {p.Processed} of {p.Total}";
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
            : filter == FilterUpdatable ? row.Status == Mp3Status.Updatable
            : filter == FilterProblems ? row.IsProblem
            : true;
        if (HideOkBox.IsChecked == true && row.Status == Mp3Status.Ok)
            return false;
        var search = SearchBox.Text.Trim();
        return show && (search.Length == 0 || row.Matches(search));
    }

    private void UpdateCount()
    {
        if (CountText is null)
            return;
        CountText.Text = _view is null || _rows.Count == 0 ? "" : $"Showing {_view.Count} of {_rows.Count}";
    }

    // --- Files and folders -------------------------------------------------------

    private IEnumerable<FileRow> SelectedRows => FileGrid.SelectedItems.OfType<FileRow>();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Ignore double-clicks on the column headers and scroll bars.
        if (e.OriginalSource is FrameworkElement { DataContext: FileRow row })
            ShowInExplorer(row.FilePath);
    }

    private void OnShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (SelectedRows.FirstOrDefault() is { } row)
            ShowInExplorer(row.FilePath);
    }

    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        var paths = SelectedRows.Select(r => r.FilePath).ToList();
        if (paths.Count > 0)
            Clipboard.SetText(string.Join(Environment.NewLine, paths));
    }

    private static void ShowInExplorer(string path)
    {
        if (File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        if (_logPath is not null && File.Exists(_logPath))
            Open(_logPath);
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text.Trim();
        if (Directory.Exists(folder))
            Open(folder);
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

    private void OnAbout(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    /// <summary>Unlike Progress&lt;T&gt;, reports on the calling thread without posting to the UI.</summary>
    private sealed class DirectProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
