# Mp3Tag.Albums

Tags and renames **one folder of mp3 files as one album**, using track lists from **Spotify** or **Discogs**. This is the engine behind **Mp3TagFromApi**. It has no UI.

The steps:

1. **Read**: `FolderReader` reads the folder's mp3 files and their current tags. It uses TagLibSharp and reads top-level files only.
2. **Look up**: `SpotifySource` or `DiscogsSource` searches for the album and loads its track list.
3. **Match**: `TrackMatcher` decides which file is which track. It first matches on title. If titles don't identify every file, it falls back to file-name order.
4. **Plan**: `AlbumTagger.Plan` works out the new tags and file names and describes every change. Nothing is written in this step.
5. **Apply**: `AlbumTagger.Apply` writes the tags (ID3v2.3), renames the files and keeps their created and modified dates. `AlbumJson.Write` saves what the source said about the album as `<album>.json` next to the files.

It also plans **mojibake-only fixes** (`AlbumTagger.PlanLetterFixes`), using `Mp3Tag.Text`.

It depends on:

- **TagLibSharp** 2.3.0
- **Mp3Tag.Text**

## API

### Example

```csharp
using Mp3Tag.Albums;
using Mp3Tag.Albums.Lookup;

var files = FolderReader.Read(folder);

using IAlbumSource source = new SpotifySource(clientId, clientSecret);
var hits = await source.SearchAsync("Gorilla James Taylor");            // or source.ParseId(pastedUrl)
var album = (await source.GetAlbumAsync(hits[0].Id)).WithoutRemasterNotes();

FolderPlan plan = AlbumTagger.Plan(folder, album, files, fixLetters: true); // throws if the track counts differ
foreach (var f in plan.Changed)
    Console.WriteLine($"{f.File.FileName}: {string.Join("; ", f.Changes)}");

AlbumTagger.Apply(plan, progress, cancellation);
AlbumJson.Write(plan);
```

### Reading: `FolderReader`, `AudioFile`

```csharp
IReadOnlyList<AudioFile> FolderReader.Read(string folder, CancellationToken cancellation = default)
AudioFile                FolderReader.ReadFile(string path)

record AudioFile(string Path)   // FileName, Title, Artist, AlbumArtist, Album, Genre, Track, TrackCount,
                                // Disc, DiscCount, Year, IsCompilation (TCMP), Duration, Error
```

`Read` sorts files the way Explorer does (`Track 2` before `Track 10`). A file that can't be read is still listed, with `Error` set, and is never changed.

### Looking up: `Lookup.IAlbumSource`

```csharp
interface IAlbumSource : IDisposable
{
    string  Name { get; }                 // "Spotify" or "Discogs"
    string? SearchProblem { get; }        // e.g. missing keys; null when searching will work
    int     SearchLimit { get; }          // the most hits a search returns; that many means there may be more
    string? ParseId(string input);        // album id from a pasted URL or id, else null
    Task<IReadOnlyList<AlbumHit>> SearchAsync(string text, CancellationToken cancellation = default);
    Task<AlbumInfo> GetAlbumAsync(string id, CancellationToken cancellation = default);
}
record AlbumHit(string Id, string Artist, string Title, int? TrackCount, string Details)
record ApiKeys { SpotifyClientId, SpotifyClientSecret, DiscogsToken }   // DiscogsToken: the personal access token
```

| Source | Keys | Ids and URLs | Notes |
|---|---|---|---|
| `SpotifySource(clientId, clientSecret)` | Always needed. They come from an app on developer.spotify.com and are used with the client credentials flow; no login is involved. | Album URL, `spotify:album:` URI, or the 22-character id | The search returns at most 10 albums (`SearchLimit`), the limit for apps in development mode. Hits include the track count. Track lists are paged until complete. |
| `DiscogsSource(token)` | Only needed for searching: a personal access token from Discogs developer settings. | A release URL or id gives `r123`. A master URL or `[m123]` gives `m123`, which resolves to the master's main release. | The search returns at most 25 releases (`SearchLimit`). Hits have no track count. Positions such as `CD2-3` and `A1`, index tracks and artist name variations are handled. `RateLimitRemaining` shows the requests left this minute. Show "Data provided by Discogs." wherever Discogs data is shown. |

Errors worth showing as they are come as `LookupException`. Network failures come as `HttpRequestException`.

### The album: `AlbumInfo`, `AlbumTrack`

```csharp
record AlbumTrack(int Disc, int Number, string Artist, string Title, TimeSpan? Duration)
record AlbumInfo   // Source, Id, Url, Artist, Title, Year, Genre, Label, CatalogNumber, Country, Tracks
                   // IsCompilation, AlbumArtist ("Various Artists" on compilations), DiscCount, Details
AlbumInfo WithoutRemasterNotes()  // "Mexico - 2019 Remaster" → "Mexico"; keeps "(Live)", "(Mono)" ...
```

### Matching: `TrackMatcher`

```csharp
TrackMatch TrackMatcher.Match(IReadOnlyList<AudioFile> files, IReadOnlyList<AlbumTrack> tracks)
record TrackMatch(MatchMethod Method, int NameMatches, IReadOnlyList<(AudioFile File, AlbumTrack Track)> Pairs)
enum MatchMethod { TrackName, FileOrder }
```

A file matches a track when its title tag or file name is the same as the track title. Leading numbers, case, accents and punctuation are ignored, and so are garbled letters (`SÃ¸ren` matches `Søren`) and version notes such as `- 2019 Remaster`. A file name that ends with the title also matches (`05 - Monique - Min Kat Den Danser Tango`).

If that doesn't identify every file exactly once, the files are given tracks 1–N in file-name order, and `Method` is `FileOrder`.

### Planning and writing: `AlbumTagger`

```csharp
FolderPlan AlbumTagger.Plan(string folder, AlbumInfo album, IReadOnlyList<AudioFile> files, bool fixLetters = false)
FolderPlan AlbumTagger.PlanLetterFixes(string folder, IReadOnlyList<AudioFile> files)
FolderPlan AlbumTagger.PlanNothing(string folder, IReadOnlyList<AudioFile> files)
FolderPlan AlbumTagger.WithHandEdits(FolderPlan plan, IReadOnlyDictionary<string, TagChange> edits)  // by file path
void       AlbumTagger.Apply(FolderPlan plan, IProgress<int>? progress = null, CancellationToken cancellation = default)
string     AlbumTagger.NewFileName(AlbumInfo album, AlbumTrack track, bool multiDisc)
string     AlbumTagger.RenameFolder(string folder, string newName)  // returns the new path

record FolderPlan(Folder, Album?, Method?, NameMatches, Files)  // Changed, LengthMismatches, Unreadable, LengthTolerance (5 s)
record FilePlan(File, Track?, Change, Changes)                  // NewFileName, Renames, HasChanges, LengthDifference, LengthDiffers, EditedByHand
record TagChange                                                // only the values that differ; null = leave as it is
                                                                // .Then(later): these values with later's in their place
```

- **`Plan`** gives each file its track's title and artist. Every file also gets:
  - album and album artist
  - track and track count
  - disc and disc count, on multi-disc albums
  - year and genre
  - the compilation flag
  - a new name: `03 - Title.mp3`, `03 - Artist - Title.mp3` on compilations, or `2-03 - ...` on multi-disc albums

  Values the file already has are left out, so a file that already matches isn't rewritten. `Changes` describes each change in words, such as `Title: Track 1 → Mexico`. `LengthDiffers` flags files whose length differs from the track's by more than 5 seconds, which can mean another edition.
- **`PlanLetterFixes`** only corrects garbled letters in the title, artist, album artist, album and genre, and in the file names.
- **`WithHandEdits`** puts values typed by hand in place of the planned ones: title, artist, album artist, album, genre, year, track and disc. In an edit, null keeps the planned value, `""` removes a text tag and `0` a number. A value equal to the file's own turns down the album's value. With an album, the new name follows the edited title, artist, track and disc. The changes are marked `(by hand)`. Start from `PlanNothing` to edit without an album or letter fixes.
- **`RenameFolder`** renames the folder within its parent, for example to `FileNames.Sanitize(album.Title)`. It refuses invalid and taken names, and handles a change of case only.
- **`Apply`** checks the new names first, so a clash changes nothing. It then writes the tags and renames the files in two steps, so files can swap names. It throws if a file it would change can't be read.

### Also

| Type | Purpose |
|---|---|
| `AlbumJson.Write(plan)`, `AlbumJson.FileNameFor(album)` | Writes `<album>.json`: album data plus, per track, the file, its old name and title and its real length. |
| `FileNames.Sanitize(name)` | Makes a valid Windows file name: `/ \ : \|` become `-`, `"` becomes `'`, `? * < >` are dropped, reserved names such as `CON` are avoided. |
| `NaturalComparer.Instance` | Sorts `Track 2` before `Track 10`. |
| `AlbumFit.Of(album, files)`, `AlbumFit.FromTrackCount(n, files)` | How an album fits a folder: `Level` is `TracksDiffer`, `LengthDiffers`, `TracksMatch` (no length listed) or `Match`. Only the total playing time is compared, within 2 % (at least 15 s). |
| `Durations.Format`, `Durations.TryParse` | Formats and parses `m:ss` and `h:mm:ss`. |
| `RemasterNotes.Remove(title)` | Strips remaster notes from a title. |

## Tests

`Mp3Tag.Tests/Albums/`.
