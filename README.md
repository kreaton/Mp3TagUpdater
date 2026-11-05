# Mp3TagUpdater

A .NET 10 console app that scans a folder of `.mp3` files, repairs tag faults that make files unreadable, and logs every file's details, tags and errors to a CSV file.

- **Safe by default:** no mp3 is changed unless you pass `--apply`.
- **Repairs touch only the tag:** the file keeps its size, the audio bytes aren't moved, and the original is kept as a `.bak` file.
- **Ready to import:** the CSV log opens in a spreadsheet or loads into a database, and it includes files that couldn't be read.

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
| `--apply` | Write repairs to disk. Without it, repairs are only reported. |
| `--no-recursive` | Scan only the top-level directory, not subfolders. |
| `--no-log` | Don't write the CSV log. |
| `-h`, `--help` | Show help. |

Check a folder and write a log, without changing any mp3:

```bash
dotnet run --project Mp3TagUpdater -- "C:\Music\Party"
```

Repair any files that can be repaired:

```bash
dotnet run --project Mp3TagUpdater -- "C:\Music\Party" --apply
```

### Example output

```
=== MP3 Tag Updater ===

Scanning 'C:\Music\Party' (recursive)...
  Failed to read 'C:\Music\Party\Damaged song.mp3': Unsupported tag version.
Found 2 mp3 file(s):
Damaged song.mp3
Healthy song.mp3

Checking unreadable files (no files will be modified). Pass --apply to repair them:
  [DRY RUN] Would repair Damaged song.mp3: Corrected ID3v2 version byte 0x23 to 0x03

Summary: OK 1, Repairable 1
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
- **Columns:** `Status`, `Errors`, `RepairActions`, `RelativePath`, `Directory`, `FileName`, `FullPath`, `SizeBytes`, `Created`, `Modified`, `Title`, `Artist`, `AlbumArtist`, `Album`, `Genre`, `Composer`, `Comment`, `Year`, `Track`, `TrackCount`, `Disc`, `Duration`, `DurationMs`, `BitrateKbps`, `SampleRateHz`, `Channels`, `TagTypes`, `PictureCount`.

| Status | Meaning |
|---|---|
| `OK` | Read without problems |
| `Warning` | Readable, but possibly corrupt or containing no MPEG audio, such as a WAV named `.mp3` (reasons are in `Errors`) |
| `Repairable` | Unreadable on disk, but `--apply` can repair it (tags shown are from the verified repair) |
| `Repaired` | Was unreadable, and this run repaired it (the original error is in `Errors`) |
| `Error` | Unreadable, with no automatic repair available |

## Filling in missing albums

`Mp3Library.UpdateMissingAlbumTags` sets a missing Album tag to the name of the folder containing the file. Its call is currently commented out in `Program.cs`, so the app doesn't change Album tags. If you re-enable it, `--apply` will write album tags as well as repairs.

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
