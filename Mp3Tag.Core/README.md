# Mp3Tag.Core

The engine behind **Mp3TagUpdater**, used by the Windows app (`Mp3TagUpdater.Wpf`). It has no UI, so it can also be used from a console program or a script.

Given a folder, it can:

- read every mp3, optionally including subfolders
- repair files whose ID3v2 tag is damaged
- fix garbled letters in tags and names
- fill in missing tags from file names and folder names
- split "Artist / Title" titles
- write a CSV log

It works file by file across a whole library. The album lookup for one folder is in `Mp3Tag.Albums` instead.

It depends on:

- **TagLibSharp** 2.3.0, to read and write tags
- **Mp3Tag.Text**, for the mojibake repairs

## API

### Running: `Mp3Library`

```csharp
var library = new Mp3Library(onMessage: m => Console.WriteLine(m.Text)); // optional per-change messages
Mp3RunResult result = library.Run(
    new Mp3RunOptions(@"D:\Music")
    {
        DryRun = true,          // the default: nothing is written, the changes are only planned
        Recursive = true,
        FixLetters = true,      // "SÃ¸ren" → "Søren" in tags and mp3 names
        FixNames = false,       // also rename every other garbled file and folder name
        FillFromFileName = true,// missing Title/Artist/Track/Disc from "1-02 Artist - Title.mp3"
        ReplaceAlbum = false,   // Album := folder name even when set (otherwise only when missing)
        SplitTitles = TitleSplitMode.Off,
    },
    progress,       // IProgress<Mp3Progress>: Phase, Processed, Total
    cancellation);  // stops before the next file
```

`Run` reads the files, then repairs them, then updates the tags, then fixes names if `FixNames` is set. A dry run and a real run take the same options, so you can show the dry run's plan and then repeat it with `DryRun = false`.

The steps can also be called on their own:

| Method | What it does |
|---|---|
| `ReadTags(root, recursive, progress, cancellation)` | Returns one `Mp3TagRecord` per mp3. An unreadable file gets status `Error`. |
| `RepairUnreadableFiles(records, dryRun, ...)` | Repairs damaged ID3v2 tags after checking the repair in memory. The original is kept as `.bak`. |
| `UpdateTags(records, options, ...)` | Plans or writes tag and name edits. |
| `PlanEdits(records, options)` (static) | Returns the planned edits per file path without touching anything. |
| `FixNames(records, options, ...)` | Renames garbled files and folders. |

### Results

```csharp
record Mp3RunResult(IReadOnlyList<Mp3TagRecord> Records, bool Cancelled) // .Summary "OK 12, Updatable 3", .HasChanges
record Mp3TagRecord   // one file: path, tags, Duration, Status, Errors, Changes, Edit
record Mp3TagEdit     // the values a write sets (null = unchanged) and what kind of fix each is
enum   Mp3EntryKind { Mp3, File, Folder }
static class Mp3Status { Ok, Warning, Error, Repairable, Repaired, Updatable, Updated }
record Mp3Progress(string Phase, int Processed, int Total)
record Mp3Message(Mp3MessageKind Kind, string Text)       // Change or Error
```

### Helpers

| Type | Purpose |
|---|---|
| `Mp3CsvLog.Write(records, rootDirectory)` | Writes `mp3-log-YYYYMMDD-HHMMSS.csv` and returns its path. |
| `FileNameTags.Parse(stem)` → `FileNameInfo(Disc, Track, Artist, Title)` | Reads `1-02 Artist - Title`, `02 Artist - Title` or `Artist - Title`. |
| `ArtistTitleSplit.Split(title)`, `.Assign(halves, artistFirst)`, `.ArtistFirstByFolder(tracks)` | Splits "Artist / Title" and works out the order for each folder. |
| `TitleSplitMode` | `Off`, `Auto` (per folder), `ArtistFirst`, `TitleFirst`. |
| `Id3v2Repair.TryRepair(bytes)` → `Id3v2RepairResult(Data, Actions)` | Repairs a damaged ID3v2 tag at the byte level. |

Namespaces:

- `Mp3Tag.Core` for the classes that do the work
- `Mp3Tag.Core.Models` for the records

The text repairs it uses (`MojibakeText`, `MisencodedText`) are in `Mp3Tag.Text`.

## Tests

`Mp3Tag.Tests`, mainly `Mp3LibraryTests.cs`. The tests generate their own mp3 files.
