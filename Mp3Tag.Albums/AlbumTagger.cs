using Mp3Tag.Text;

namespace Mp3Tag.Albums;

/// <summary>New values for a file's tags and name. A null value is left as it is.</summary>
public sealed record TagChange
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? AlbumArtist { get; init; }
    public string? Album { get; init; }
    public string? Genre { get; init; }
    public uint? Track { get; init; }
    public uint? TrackCount { get; init; }
    public uint? Disc { get; init; }
    public uint? DiscCount { get; init; }
    public uint? Year { get; init; }

    /// <summary>Sets or clears the ID3v2 TCMP "part of a compilation" flag.</summary>
    public bool? Compilation { get; init; }

    /// <summary>The new file name, extension included.</summary>
    public string? FileName { get; init; }

    public bool ChangesTags =>
        Title is not null || Artist is not null || AlbumArtist is not null || Album is not null || Genre is not null
        || Track is not null || TrackCount is not null || Disc is not null || DiscCount is not null || Year is not null
        || Compilation is not null;

    /// <summary>These values, with the ones <paramref name="later"/> sets taking their place.</summary>
    public TagChange Then(TagChange later) => new()
    {
        Title = later.Title ?? Title,
        Artist = later.Artist ?? Artist,
        AlbumArtist = later.AlbumArtist ?? AlbumArtist,
        Album = later.Album ?? Album,
        Genre = later.Genre ?? Genre,
        Track = later.Track ?? Track,
        TrackCount = later.TrackCount ?? TrackCount,
        Disc = later.Disc ?? Disc,
        DiscCount = later.DiscCount ?? DiscCount,
        Year = later.Year ?? Year,
        Compilation = later.Compilation ?? Compilation,
        FileName = later.FileName ?? FileName,
    };
}

/// <summary>What will happen to one file: which album track it is (if any) and what changes.</summary>
/// <param name="Changes">The changes in words, such as "Title: Track 1 → Mexico". Empty when nothing changes.</param>
public sealed record FilePlan(AudioFile File, AlbumTrack? Track, TagChange Change, IReadOnlyList<string> Changes)
{
    /// <summary>Some of the values were typed by hand (see <see cref="AlbumTagger.WithHandEdits"/>).</summary>
    public bool EditedByHand { get; init; }

    public string NewFileName => Change.FileName ?? File.FileName;

    public bool Renames => Change.FileName is not null;

    public bool HasChanges => Changes.Count > 0;

    /// <summary>File length minus the album's track length, or null when the source has no length.</summary>
    public TimeSpan? LengthDifference => Track?.Duration is { } d && File.Error is null ? File.Duration - d : null;

    /// <summary>The file is much longer or shorter than the track, so it is probably another track or edition.</summary>
    public bool LengthDiffers => LengthDifference is { } d && d.Duration() > FolderPlan.LengthTolerance;
}

/// <summary>The full set of changes for a folder, worked out before anything is written.</summary>
/// <param name="Album">The album the files are tagged from, or null when only garbled letters are fixed.</param>
/// <param name="Method">How files were matched to tracks, or null without an album.</param>
public sealed record FolderPlan(string Folder, AlbumInfo? Album, MatchMethod? Method, int NameMatches, IReadOnlyList<FilePlan> Files)
{
    /// <summary>A file this much longer or shorter than the listed track is probably the wrong track or edition.</summary>
    public static readonly TimeSpan LengthTolerance = TimeSpan.FromSeconds(5);

    public IReadOnlyList<FilePlan> LengthMismatches => Files.Where(f => f.LengthDiffers).ToList();

    public IReadOnlyList<FilePlan> Changed => Files.Where(f => f.HasChanges).ToList();

    public IReadOnlyList<FilePlan> Unreadable => Files.Where(f => f.File.Error is not null).ToList();
}

/// <summary>Plans new tags and names for a folder of mp3 files, and writes them with TagLibSharp.</summary>
public static class AlbumTagger
{
    /// <summary>
    /// Plans tagging the files as <paramref name="album"/>: every file gets its track's title and artist, the album,
    /// track number, year and a name such as "03 - Title.mp3". With <paramref name="fixLetters"/>, garbled letters
    /// are also corrected in the tags the album doesn't replace (the genre).
    /// </summary>
    /// <exception cref="InvalidOperationException">The album and the folder have different numbers of tracks.</exception>
    public static FolderPlan Plan(string folder, AlbumInfo album, IReadOnlyList<AudioFile> files, bool fixLetters = false)
    {
        if (files.Count != album.Tracks.Count)
            throw new InvalidOperationException($"The album has {album.Tracks.Count} tracks but the folder has {files.Count} mp3 files.");

        var match = TrackMatcher.Match(files, album.Tracks);
        var multiDisc = album.DiscCount > 1;
        var plans = match.Pairs.Select(p =>
        {
            var (file, track) = p;
            var wanted = new TagChange
            {
                Title = track.Title,
                Artist = track.Artist,
                AlbumArtist = album.AlbumArtist,
                Album = album.Title,
                Genre = !string.IsNullOrWhiteSpace(album.Genre) ? album.Genre : fixLetters ? TextRepair.Fix(file.Genre) : null,
                Track = (uint)track.Number,
                TrackCount = (uint)album.Tracks.Count(t => t.Disc == track.Disc),
                Disc = multiDisc ? (uint)track.Disc : null,
                DiscCount = multiDisc ? (uint)album.DiscCount : null,
                Year = album.Year > 0 ? (uint)album.Year : null,
                Compilation = album.IsCompilation,
                FileName = NewFileName(album, track, multiDisc),
            };
            return Compare(file, track, wanted);
        }).ToList();

        return new FolderPlan(Path.GetFullPath(folder), album, match.Method, match.NameMatches, plans);
    }

    /// <summary>
    /// Plans correcting garbled letters ("SÃ¸ren" → "Søren") in the title, artist, album artist, album and genre
    /// tags and in the file names. Nothing else changes. A letter that was lost is recovered when the file name
    /// or folder name spells the same word correctly.
    /// </summary>
    public static FolderPlan PlanLetterFixes(string folder, IReadOnlyList<AudioFile> files)
    {
        var folderName = new DirectoryInfo(folder).Name;
        var plans = files.Select(file =>
        {
            if (file.Error is not null)
                return new FilePlan(file, null, new TagChange(), []);

            var stem = Path.GetFileNameWithoutExtension(file.Path);
            var title = TextRepair.Fix(file.Title, stem);
            var artist = TextRepair.Fix(file.Artist, stem);
            var album = TextRepair.Fix(file.Album, folderName, stem);
            var newStem = TextRepair.Fix(stem, title ?? file.Title, artist ?? file.Artist, album ?? file.Album);
            var wanted = new TagChange
            {
                Title = title,
                Artist = artist,
                AlbumArtist = TextRepair.Fix(file.AlbumArtist, stem, folderName),
                Album = album,
                Genre = TextRepair.Fix(file.Genre),
                FileName = newStem is null ? null : FileNames.Sanitize(newStem) + Path.GetExtension(file.Path),
            };
            return Compare(file, null, wanted);
        }).ToList();

        return new FolderPlan(Path.GetFullPath(folder), null, null, 0, plans);
    }

    /// <summary>A plan that changes nothing: the starting point for hand edits without an album or letter fixes.</summary>
    public static FolderPlan PlanNothing(string folder, IReadOnlyList<AudioFile> files) =>
        new(Path.GetFullPath(folder), null, null, 0, files.Select(f => new FilePlan(f, null, new TagChange(), [])).ToList());

    /// <summary>
    /// The plan with values typed by hand in place of the planned ones, by file path. In an edit, null keeps the
    /// planned value, "" removes a text tag and 0 a number. A value equal to the file's own keeps the file as it is,
    /// which is how a value from the album is turned down. With an album, the new file name follows the edited
    /// title, artist, track and disc. Files that can't be read are left out.
    /// </summary>
    public static FolderPlan WithHandEdits(FolderPlan plan, IReadOnlyDictionary<string, TagChange> edits)
    {
        if (edits.Count == 0)
            return plan;

        var files = plan.Files.Select(p =>
        {
            if (!edits.TryGetValue(p.File.Path, out var edit) || p.File.Error is not null)
                return p;

            // Only the tags a person edits; the counts, compilation flag and name come from the plan.
            var hand = new TagChange
            {
                Title = edit.Title, Artist = edit.Artist, AlbumArtist = edit.AlbumArtist, Album = edit.Album,
                Genre = edit.Genre, Track = edit.Track, Disc = edit.Disc, Year = edit.Year,
            };
            var wanted = p.Change.Then(hand);
            if (plan.Album is { } album && p.Track is { } track)
            {
                var named = track with
                {
                    Title = Final(wanted.Title, p.File.Title) ?? track.Title,
                    Artist = Final(wanted.Artist, p.File.Artist) ?? track.Artist,
                    Number = (int)(wanted.Track ?? p.File.Track),
                    Disc = (int)(wanted.Disc ?? p.File.Disc),
                };
                wanted = wanted with { FileName = named.Number > 0 ? NewFileName(album, named, album.DiscCount > 1) : null };
            }
            return Compare(p.File, p.Track, wanted, hand) with { EditedByHand = true };
        }).ToList();

        return plan with { Files = files };

        // The value the tag will have: the wanted one, or the file's own; empty counts as none.
        static string? Final(string? wanted, string? current) => (wanted ?? current) is { Length: > 0 } v ? v : null;
    }

    /// <summary>"03 - Title.mp3", or "03 - Artist - Title.mp3" on compilations, with "2-03" on multi-disc albums.</summary>
    public static string NewFileName(AlbumInfo album, AlbumTrack track, bool multiDisc)
    {
        var number = multiDisc ? $"{track.Disc}-{track.Number:D2}" : $"{track.Number:D2}";
        var body = album.IsCompilation ? $"{track.Artist} - {track.Title}" : track.Title;
        return FileNames.Sanitize($"{number} - {body}") + ".mp3";
    }

    /// <summary>Keeps only the wanted values that differ from the file's, and describes them.</summary>
    /// <param name="hand">The values typed by hand, marked "(by hand)" in the descriptions.</param>
    private static FilePlan Compare(AudioFile file, AlbumTrack? track, TagChange wanted, TagChange? hand = null)
    {
        if (file.Error is not null)
            return new FilePlan(file, track, new TagChange(), []);

        var changes = new List<string>();

        string? Text(string label, string? current, string? value, string? byHand)
        {
            // An empty value removes the tag, so it changes nothing where there is no tag.
            if (value is null || string.Equals(current ?? "", value, StringComparison.Ordinal))
                return null;
            var shown = value.Length == 0 ? "(removed)" : value;
            var note = byHand is not null ? " (by hand)" : "";
            changes.Add(string.IsNullOrEmpty(current) ? $"{label}: {shown}{note}" : $"{label}: {current} → {shown}{note}");
            return value;
        }

        uint? Number(string label, uint current, uint? value, uint? byHand)
        {
            if (value is null || current == value)
                return null;
            var shown = value == 0 ? "(removed)" : value.ToString();
            var note = byHand is not null ? " (by hand)" : "";
            changes.Add(current == 0 ? $"{label}: {shown}{note}" : $"{label}: {current} → {shown}{note}");
            return value;
        }

        bool? compilation = null;
        if (wanted.Compilation is { } c && c != file.IsCompilation)
        {
            compilation = c;
            changes.Add(c ? "Compilation: on" : "Compilation: off");
        }

        var change = new TagChange
        {
            Title = Text("Title", file.Title, wanted.Title, hand?.Title),
            Artist = Text("Artist", file.Artist, wanted.Artist, hand?.Artist),
            AlbumArtist = Text("Album artist", file.AlbumArtist, wanted.AlbumArtist, hand?.AlbumArtist),
            Album = Text("Album", file.Album, wanted.Album, hand?.Album),
            Genre = Text("Genre", file.Genre, wanted.Genre, hand?.Genre),
            Track = Number("Track", file.Track, wanted.Track, hand?.Track),
            TrackCount = Number("Tracks", file.TrackCount, wanted.TrackCount, null),
            Disc = Number("Disc", file.Disc, wanted.Disc, hand?.Disc),
            DiscCount = Number("Discs", file.DiscCount, wanted.DiscCount, null),
            Year = Number("Year", file.Year, wanted.Year, hand?.Year),
            Compilation = compilation,
            FileName = Text("Name", file.FileName, wanted.FileName, null),
        };
        return new FilePlan(file, track, change, changes);
    }

    /// <summary>
    /// Writes the tags, then renames. Checks the new names first, so a clash changes nothing. The files'
    /// created and modified dates are kept. <paramref name="progress"/> gets the number of files written.
    /// </summary>
    public static void Apply(FolderPlan plan, IProgress<int>? progress = null, CancellationToken cancellation = default)
    {
        var changed = plan.Changed;
        if (changed.FirstOrDefault(f => f.File.Error is not null) is { } unreadable)
            throw new InvalidOperationException($"\"{unreadable.File.FileName}\" can't be read, so it can't be changed.");

        var ours = plan.Files.Select(f => Path.GetFullPath(f.File.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in changed.Where(f => f.Renames))
        {
            var target = Path.Combine(plan.Folder, f.NewFileName);
            if (File.Exists(target) && !ours.Contains(target))
                throw new IOException($"\"{f.NewFileName}\" already exists and isn't one of the album's files.");
        }
        var clash = plan.Files.GroupBy(f => f.NewFileName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (clash is not null)
            throw new IOException($"Two files would both be named \"{clash.Key}\".");

        // ID3v2.3 is what Windows Explorer and most players read reliably.
        TagLib.Id3v2.Tag.DefaultVersion = 3;
        TagLib.Id3v2.Tag.ForceDefaultVersion = true;
        var done = 0;
        foreach (var f in changed)
        {
            cancellation.ThrowIfCancellationRequested();
            if (f.Change.ChangesTags)
                WriteTags(f.File.Path, f.Change);
            progress?.Report(++done);
        }

        // Rename in two steps, so two files can swap names ("01 - B" ↔ "02 - A") without colliding.
        // Not cancellable: stopping half-way would leave files with temporary names.
        var moves = changed.Where(f => f.Renames)
            .Select(f => (Plan: f, Temp: Path.Combine(plan.Folder, $".{Guid.NewGuid():N}.renaming")))
            .ToList();
        foreach (var (f, temp) in moves) File.Move(f.File.Path, temp);
        foreach (var (f, temp) in moves) File.Move(temp, Path.Combine(plan.Folder, f.NewFileName));
    }

    /// <summary>
    /// Renames <paramref name="folder"/> to <paramref name="newName"/> in the same parent folder, and returns
    /// its new path. A change of case only ("abba" → "ABBA") goes through a temporary name, which Windows needs.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="newName"/> isn't a valid folder name.</exception>
    /// <exception cref="IOException">The name is taken, or a file in the folder is in use.</exception>
    public static string RenameFolder(string folder, string newName)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (newName.Length == 0 || newName != FileNames.Sanitize(newName, fallback: ""))
            throw new ArgumentException($"\"{newName}\" isn't a valid folder name.", nameof(newName));
        var parent = Path.GetDirectoryName(folder) ?? throw new IOException("A drive's root folder can't be renamed.");
        var target = Path.Combine(parent, newName);
        if (string.Equals(target, folder, StringComparison.Ordinal))
            return folder;

        if (string.Equals(target, folder, StringComparison.OrdinalIgnoreCase))
        {
            var temp = Path.Combine(parent, $".{Guid.NewGuid():N}.renaming");
            Directory.Move(folder, temp);
            Directory.Move(temp, target);
        }
        else
        {
            if (Directory.Exists(target) || File.Exists(target))
                throw new IOException($"There is already a folder or file named \"{newName}\" next to \"{Path.GetFileName(folder)}\".");
            Directory.Move(folder, target);
        }
        return target;
    }

    private static void WriteTags(string path, TagChange c)
    {
        var created = File.GetCreationTimeUtc(path);
        var modified = File.GetLastWriteTimeUtc(path);

        using (var file = TagLib.File.Create(path))
        {
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
            var tag = file.Tag; // writes to every tag in the file (ID3v2, and ID3v1 if present)

            // An empty value removes the tag; 0 removes a number.
            if (c.Title is { } title) tag.Title = title.Length > 0 ? title : null;
            if (c.Artist is { } artist) tag.Performers = artist.Length > 0 ? [artist] : [];
            if (c.AlbumArtist is { } albumArtist) tag.AlbumArtists = albumArtist.Length > 0 ? [albumArtist] : [];
            if (c.Album is { } album) tag.Album = album.Length > 0 ? album : null;
            if (c.Genre is { } genre) tag.Genres = genre.Length > 0 ? [genre] : [];
            if (c.Track is { } track) tag.Track = track;
            if (c.TrackCount is { } trackCount) tag.TrackCount = trackCount;
            if (c.Disc is { } disc) tag.Disc = disc;
            if (c.DiscCount is { } discCount) tag.DiscCount = discCount;
            if (c.Year is { } year) tag.Year = year;

            // TCMP is the de-facto "part of a compilation" flag (iTunes, Windows, foobar2000).
            if (c.Compilation == true) id3.SetTextFrame("TCMP", "1");
            else if (c.Compilation == false) id3.RemoveFrames("TCMP");

            file.Save();
        }

        // Keep the file's dates, so re-tagging doesn't make old music look new. (Renaming keeps them too.)
        File.SetCreationTimeUtc(path, created);
        File.SetLastWriteTimeUtc(path, modified);
    }
}
