using System.IO;
using System.Windows;
using Mp3Tag.Albums;

namespace Mp3TagFromApi.Wpf;

/// <summary>
/// Asks before Apply, listing what will happen. With an album, it also offers to rename the folder to the
/// album's name, which can be edited. <see cref="RenameTo"/> holds the new folder name when one was chosen.
/// </summary>
public partial class ApplyWindow : Window
{
    private readonly string _folder;

    /// <summary>The folder's new name (not a path) when the dialog returns true and it should be renamed.</summary>
    public string? RenameTo { get; private set; }

    /// <param name="lines">What will happen, one line each.</param>
    /// <param name="risky">Something needs checking first, so Enter doesn't apply.</param>
    /// <param name="suggestedName">The folder name to offer, or null to not offer a rename.</param>
    /// <param name="renameTicked">Whether the rename starts ticked: not once the user has settled the name.</param>
    public ApplyWindow(string folder, IEnumerable<string> lines, bool risky, string? suggestedName, bool renameTicked = true)
    {
        InitializeComponent();
        _folder = folder;

        FolderText.Text = folder;
        LinesList.ItemsSource = lines.ToList();
        WarningIcon.Visibility = risky ? Visibility.Visible : Visibility.Collapsed;

        if (suggestedName is not null)
        {
            RenamePanel.Visibility = Visibility.Visible;
            FolderNameBox.Text = suggestedName;
            CurrentNameText.Text = $"Now: {Path.GetFileName(folder)}";
            RenameBox.IsChecked = renameTicked;
        }

        // When something needs checking, Enter cancels rather than applies.
        ApplyButton.IsDefault = !risky;
        CancelButton.IsDefault = risky;
        Loaded += (_, _) => (risky ? CancelButton : ApplyButton).Focus();
    }

    /// <summary>The name the album suggests for the folder, or null when the folder already has it or can't be renamed.</summary>
    public static string? SuggestedName(string folder, AlbumInfo? album)
    {
        if (album is null || Path.GetDirectoryName(folder) is null)
            return null;
        var name = FileNames.Sanitize(album.Title, fallback: "");
        return name.Length == 0 || name == Path.GetFileName(folder) ? null : name;
    }

    private void OnRenameChanged(object sender, RoutedEventArgs e)
    {
        if (FolderNameBox is null)
            return;
        FolderNameBox.IsEnabled = RenameBox.IsChecked == true;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        RenameTo = null;
        if (RenamePanel.Visibility == Visibility.Visible && RenameBox.IsChecked == true)
        {
            var name = FolderNameBox.Text.Trim();
            if (FolderNameProblem(name) is { } problem)
            {
                ErrorText.Text = problem;
                ErrorText.Visibility = Visibility.Visible;
                FolderNameBox.Focus();
                FolderNameBox.SelectAll();
                return;
            }
            if (name != Path.GetFileName(_folder))
                RenameTo = name;
        }
        DialogResult = true;
    }

    /// <summary>Why <paramref name="name"/> can't be the folder's new name, or null when it can.</summary>
    private string? FolderNameProblem(string name)
    {
        if (name.Length == 0)
            return "Type the folder's new name, or clear the tick to keep the name it has.";
        if (name != FileNames.Sanitize(name, fallback: ""))
            return $"\"{name}\" isn't a valid folder name: it can't contain \\ / : * ? \" < > |, end with a dot, or be a name such as CON. Try \"{FileNames.Sanitize(name)}\".";
        var target = Path.Combine(Path.GetDirectoryName(_folder)!, name);
        // A different spelling of the same name ("abba" → "ABBA") renames the same folder.
        if (Directory.Exists(target) && !string.Equals(target, _folder, StringComparison.OrdinalIgnoreCase) || File.Exists(target))
            return $"There is already a folder or file named \"{name}\" next to this one.";
        return null;
    }
}
