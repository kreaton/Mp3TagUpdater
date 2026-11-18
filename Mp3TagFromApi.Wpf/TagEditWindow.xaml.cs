using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Mp3Tag.Albums;

namespace Mp3TagFromApi.Wpf;

/// <summary>The tags a file will have after Apply: what is planned for it, or its own tags where nothing is.</summary>
public sealed record TagValues(string FileName, string? Title, string? Artist, string? Album, string? AlbumArtist,
    string? Genre, uint Year, uint Track, uint Disc);

/// <summary>
/// Edits the tags of one or more files by hand. Nothing is written here: the values join the plan, and Apply
/// writes them. With several files, a field left as it is keeps each file's own value.
/// </summary>
public partial class TagEditWindow : Window
{
    private readonly List<Field> _fields;

    /// <summary>The fields that were changed when the dialog returns true. Null fields are left as they are.</summary>
    public TagChange? Edit { get; private set; }

    public TagEditWindow(IReadOnlyList<TagValues> files)
    {
        InitializeComponent();
        var single = files.Count == 1;

        HeaderText.Text = single ? files[0].FileName : $"{files.Count} files";
        SubText.Text = single
            ? "The values Apply will write"
            : string.Join(", ", files.Take(3).Select(f => f.FileName)) + (files.Count > 3 ? $" and {files.Count - 3} more" : "");
        SubText.ToolTip = string.Join(Environment.NewLine, files.Select(f => f.FileName));
        FooterText.Text = (single
            ? "Empty a field to remove that tag. With an album, the file name follows the title and track. "
            : "Only the fields you change are set, in every file. ")
            + "Nothing is written until you apply.";

        _fields =
        [
            new(TitleBox, files.Select(f => f.Title), perFile: true),
            new(ArtistBox, files.Select(f => f.Artist)),
            new(AlbumBox, files.Select(f => f.Album)),
            new(AlbumArtistBox, files.Select(f => f.AlbumArtist)),
            new(GenreBox, files.Select(f => f.Genre)),
            new(YearBox, files.Select(f => Number(f.Year)), number: true),
            new(TrackBox, files.Select(f => Number(f.Track)), number: true, perFile: true),
            new(DiscBox, files.Select(f => Number(f.Disc)), number: true),
        ];

        // Titles and track numbers belong to one file, so setting them on several at once would be a mistake.
        foreach (var field in _fields.Where(f => f.PerFile && !single))
        {
            field.Box.IsEnabled = false;
            field.Box.ToolTip = "Edit one file at a time to change this.";
        }
        foreach (var field in _fields.Where(f => f.Mixed && f.Box.IsEnabled))
            ShowMixedHint(field.Box, field.IsNumber ? "Mixed" : "Different values");

        Loaded += (_, _) =>
        {
            var first = _fields.First(f => f.Box.IsEnabled).Box;
            first.Focus();
            first.SelectAll();
        };

        static string? Number(uint value) => value == 0 ? null : value.ToString(CultureInfo.CurrentCulture);
    }

    /// <summary>Shows placeholder text in an empty box whose files differ.</summary>
    private static void ShowMixedHint(TextBox box, string text)
    {
        var hint = new TextBlock
        {
            Text = text,
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(box.Margin.Left + 12, box.Margin.Top, 0, box.Margin.Bottom),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        Grid.SetRow(hint, Grid.GetRow(box));
        Grid.SetColumn(hint, Grid.GetColumn(box));
        ((Panel)box.Parent).Children.Add(hint);
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.ToolTip = "The files have different values. Leave it empty to keep them, or type a value for all of them.";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var changed = _fields.Where(f => f.Changed).ToList();
        foreach (var field in changed.Where(f => f.IsNumber))
        {
            if (field.Text.Length > 0 && !uint.TryParse(field.Text, NumberStyles.None, CultureInfo.CurrentCulture, out _))
            {
                ErrorText.Text = "Year, track and disc must be whole numbers, or empty to remove them.";
                ErrorText.Visibility = Visibility.Visible;
                field.Box.Focus();
                field.Box.SelectAll();
                return;
            }
        }

        Edit = new TagChange
        {
            Title = Text(TitleBox),
            Artist = Text(ArtistBox),
            Album = Text(AlbumBox),
            AlbumArtist = Text(AlbumArtistBox),
            Genre = Text(GenreBox),
            Year = Number(YearBox),
            Track = Number(TrackBox),
            Disc = Number(DiscBox),
        };
        DialogResult = true;

        string? Text(TextBox box) => changed.FirstOrDefault(f => f.Box == box)?.Text;

        // An empty number removes the tag, which is 0 in the plan.
        uint? Number(TextBox box) => Text(box) is { } text ? text.Length == 0 ? 0 : uint.Parse(text, NumberStyles.None, CultureInfo.CurrentCulture) : null;
    }

    /// <summary>One text box, with the value it started with, or none if the files differ.</summary>
    private sealed class Field
    {
        public TextBox Box { get; }
        public bool IsNumber { get; }
        public bool PerFile { get; }
        public bool Mixed { get; }
        private readonly string _initial;

        public Field(TextBox box, IEnumerable<string?> values, bool number = false, bool perFile = false)
        {
            Box = box;
            IsNumber = number;
            PerFile = perFile;
            var distinct = values.Select(v => v ?? "").Distinct(StringComparer.Ordinal).ToList();
            Mixed = distinct.Count > 1;
            _initial = Mixed ? "" : distinct[0];
            box.Text = _initial;
        }

        public string Text => Box.Text.Trim();

        /// <summary>
        /// A field that differs between the files counts as changed only once something is typed in it.
        /// Surrounding spaces are ignored, so a value isn't changed just because of them.
        /// </summary>
        public bool Changed => Box.IsEnabled && (Mixed ? Text.Length > 0 : Text != _initial.Trim());
    }
}
