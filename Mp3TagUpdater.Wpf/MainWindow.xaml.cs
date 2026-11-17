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

    public MainWindow()
    {
        InitializeComponent();
        _refresh.Tick += (_, _) => ShowProgress();
        UpdateButtons();
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

    private Mp3RunOptions CurrentOptions(bool dryRun) =>
        new(Path.TrimEndingDirectorySeparator(FolderBox.Text.Trim())) { DryRun = dryRun, Recursive = RecursiveBox.IsChecked == true };

    private void UpdateButtons()
    {
        // Checked handlers fire while the XAML is still loading.
        if (ScanButton is null || FolderBox is null || RecursiveBox is null)
            return;

        var idle = _cancellation is null;
        ScanButton.IsEnabled = idle && !string.IsNullOrWhiteSpace(FolderBox.Text);

        // Apply only writes what the last scan showed, so the folder and options must still match it.
        var pending = _rows.Count(r => r.HasChanges && r.Record.Status is Mp3Status.Repairable or Mp3Status.Updatable);
        var scanStillValid = _scanned is not null && _scanned == CurrentOptions(dryRun: true);
        ApplyButton.IsEnabled = idle && scanStillValid && pending > 0;
        ApplyButton.Content = scanStillValid && pending > 0 ? $"Apply {pending} {(pending == 1 ? "change" : "changes")}..." : "Apply changes...";

        CancelButton.IsEnabled = !idle;
        // Installing an update restarts the app, so it must not be reachable during a run.
        AboutButton.IsEnabled = idle;
        FolderBox.IsEnabled = RecursiveBox.IsEnabled = LogBox.IsEnabled = idle;
    }

    // --- Running -----------------------------------------------------------------

    private async void OnScan(object sender, RoutedEventArgs e) => await RunAsync(CurrentOptions(dryRun: true));

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        var options = CurrentOptions(dryRun: false);
        var changed = _rows.Where(r => r.HasChanges).ToList();
        var repairs = changed.Count(r => r.Record.Status == Mp3Status.Repairable);
        var albums = changed.Count(r => r.AlbumPending);

        var lines = new List<string>();
        if (repairs > 0)
            lines.Add($"•  {Files(repairs)} repaired. Each original is kept next to it as .bak.");
        if (albums > 0)
            lines.Add($"•  {Files(albums)} given an Album tag from the folder name.");

        var answer = MessageBox.Show(
            this,
            $"This writes to {changed.Count} mp3 {(changed.Count == 1 ? "file" : "files")} in\n\n{options.Directory}\n\n{string.Join("\n", lines)}\n\n" +
            "Modified dates are kept. The folder is read again first, so the changes are worked out from the files as they are now.\n\nApply the changes?",
            "Apply changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes)
            await RunAsync(options);

        static string Files(int n) => n == 1 ? "1 file is" : $"{n} files are";
    }

    private async Task RunAsync(Mp3RunOptions options)
    {
        if (!Directory.Exists(options.Directory))
        {
            MessageBox.Show(this, $"The folder doesn't exist:\n\n{options.Directory}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

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
            return;
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
        var show = FilterBox.SelectedIndex switch
        {
            1 => row.HasChanges,
            2 => row.IsProblem,
            _ => true
        };
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
