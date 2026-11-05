# Mp3TagUpdater

A .NET 10 console app that scans a folder of `.mp3` files, repairs tag faults that make files unreadable, and fills in missing Album tags from the folder name.

- **Safe by default:** no mp3 is changed unless you pass `--apply`.
- **Quiet console:** it lists only proposed or actual changes, followed by a summary.
- **Full CSV log:** every file's details, tags and errors go in a log that opens in a spreadsheet or loads into a database. This includes files that couldn't be read.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- The [TagLibSharp](https://www.nuget.org/packages/TagLibSharp) NuGet package, which is restored automatically on build

## Build

```bash
dotnet build Mp3TagUpdater/Mp3TagUpdater.csproj
```

## Usage

```
Mp3TagUpdater <directory> [--apply] [--no-recursive] [--no-log]
```

| Option | Effect |
|---|---|
| `--apply` | Write repairs and Album tags to disk. Without it, changes are only proposed. |
| `--no-recursive` | Scan only the top-level directory, not subfolders. |
| `--no-log` | Don't write the CSV log. |
| `-h`, `--help` | Show help. |

Check a folder and write a log, without changing any mp3:

```bash
dotnet run --project Mp3TagUpdater -- "C:\Music\Party"
```

Write the changes:

```bash
dotnet run --project Mp3TagUpdater -- "C:\Music\Party" --apply
```

### Example output

```
  [DRY RUN] Would repair Damaged song.mp3: Corrected ID3v2 version byte 0x23 to 0x03
  [DRY RUN] Would set Album to "Party": No album.mp3
Summary: OK 1, Repairable 1, Updatable 1
No files were modified. Pass --apply to write these changes.
Log written to C:\Music\Party\mp3-log-20261105-110130.csv
```

## Repairing files

A file that TagLib can't open is checked for these faults:

| Fault | Repair |
|---|---|
| Empty `APIC` or `GEOB` frame (error: *A picture frame must contain at least 5 bytes*) | Remove the frame |
| Bad version byte such as `0x23` (error: *Unsupported tag version*), when the frames parse cleanly as v2.3 or v2.4 | Correct the version byte |
| Stated tag size runs past the padding into the first MPEG frame | Correct the stated size |

Each repair is verified in memory before anything is written. With `--apply`, the file is replaced atomically, its modified time is kept, and the original is saved as `<name>.mp3.bak`. An existing `.bak` is never overwritten.

Files with any other fault are left alone and logged as `Error`.

## CSV log

Every run writes `mp3-log-YYYYMMDD-HHMMSS.csv` to the top of the scanned directory, including dry runs. It has one row per mp3.

- **Format:** RFC 4180, comma-separated, UTF-8 with BOM.
- **Values:** numbers are whole and dates are `yyyy-MM-dd HH:mm:ss`, so they don't depend on locale. A missing value is an empty cell.
- **Columns:** `Status`, `Errors`, `Changes`, `RelativePath`, `Directory`, `FileName`, `FullPath`, `SizeBytes`, `Created`, `Modified`, `Title`, `Artist`, `AlbumArtist`, `Album`, `Genre`, `Composer`, `Comment`, `Year`, `Track`, `TrackCount`, `Disc`, `Duration`, `DurationMs`, `BitrateKbps`, `SampleRateHz`, `Channels`, `TagTypes`, `PictureCount`.

| Status | Meaning |
|---|---|
| `OK` | Read without problems |
| `Warning` | Readable, but possibly corrupt or containing no MPEG audio, such as a WAV named `.mp3` (reasons are in `Errors`) |
| `Repairable` | Unreadable on disk, but `--apply` can repair it (tags shown are from the verified repair) |
| `Repaired` | Was unreadable, and this run repaired it (the original error is in `Errors`) |
| `Updatable` | Readable and missing its Album tag, which `--apply` would fill in |
| `Updated` | Was missing its Album tag, and this run filled it in |
| `Error` | Unreadable, with no automatic repair available |

## Filling in missing albums

A missing Album tag (empty or only whitespace) is set to the name of the folder directly containing the file. A file directly in the scanned directory therefore gets that directory's name. A file that is repaired in the same run can also get an album.

Files with `Error` or `Warning` status are skipped, so tags are never rewritten on a file that may be damaged or not really an mp3.

Each album is saved to a temporary copy that is then swapped in, so a failed save can't damage the original. The modified time is kept, but no `.bak` is made. The `Changes` column of the log records which files were updated.

## Project structure

```
Mp3TagUpdater.slnx
Mp3TagUpdater/
├── Mp3TagUpdater.csproj
├── Program.cs         # CLI entry point
├── Mp3TagRecord.cs    # immutable record of one file's details, tags and status
├── Mp3Library.cs      # scanning, reading, repairing and updating
├── Id3v2Repair.cs     # byte-level repair of damaged ID3v2 tags
└── Mp3CsvLog.cs       # CSV log writer
```
