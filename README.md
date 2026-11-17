# Mp3TagUpdater

[![Release](https://img.shields.io/github/v/release/kreaton/Mp3TagUpdater)](https://github.com/kreaton/Mp3TagUpdater/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
![Windows](https://img.shields.io/badge/platform-Windows-lightgrey)
![.NET 10](https://img.shields.io/badge/.NET-10-lightgrey)
![WPF](https://img.shields.io/badge/UI-WPF-lightgrey)

![Mp3TagUpdater: repair broken mp3 tags and fill in missing albums from the folder name](images/banner.png)

**Fix the mp3 files your player can't read, and give every song an album.**

Mp3TagUpdater checks a folder of `.mp3` files, repairs tag faults that make files unreadable, and fills in missing Album tags from the folder name. It can also correct Danish letters that show as two characters ("Ã¸" for "ø"), and move the artist out of titles such as "Artist / Title".

- **Safe by default:** a scan only shows what would change. Nothing is written until you choose **Apply**.
- **Every file at a glance:** status, album, artist, title, length, and what would change or went wrong.
- **Full CSV log:** every file's details, tags and errors in a log that opens in a spreadsheet or loads into a database, including files that couldn't be read.

## Install

**[Download the installer](https://github.com/kreaton/Mp3TagUpdater/releases/latest/download/Mp3TagUpdater-win-Setup.exe)** and run it. If your PC doesn't have the .NET 10 Desktop Runtime yet, the installer adds it.

The installer isn't code-signed, so Windows may warn you about it. To continue, choose **More info → Run anyway**.

To get a newer version later, click the ⓘ button and choose **Check for updates**. To remove the app, use **Settings → Apps → Installed apps** in Windows.

## How to use it

1. Choose a **Folder** of mp3 files with **Browse...**, or drop a folder on the window.
2. Choose **Scan**. Every mp3 is read and listed, and nothing is changed. A changed album is shown as `→ Folder name`.
3. Check the list. **Show** narrows it to the files with changes or to errors and warnings, and **Search** finds a file by path, title, artist or album.
4. Choose **Apply N changes...** and confirm. The folder is read again and the changes are written.

**Cancel** stops after the current file. Double-click a file to show it in Explorer, or right-click to copy its path.

Untick **Include subfolders** to check only the top-level folder. Untick **Write CSV log** to skip the log. **Open log** opens the log of the last run. Tick **Fix æ, ø, å** and choose **Split titles** to use the options described below. A new value is shown as `→ value` in the list until it is applied.

The folder and the options are remembered for the next start, in `%APPDATA%\Mp3TagUpdater\settings.json`.

| Status | Meaning |
|---|---|
| `OK` | Read without problems |
| `Warning` | Readable, but possibly corrupt or containing no MPEG audio, such as a WAV named `.mp3` |
| `Repairable` | Unreadable, but **Apply** can repair it (the tags shown are from the verified repair) |
| `Repaired` | Was unreadable, and this run repaired it |
| `Updatable` | Readable, and **Apply** would change its tags or its name |
| `Updated` | This run changed its tags or its name |
| `Error` | Unreadable, with no automatic repair available |

## Repairing files

A file that can't be opened is checked for these faults:

| Fault | Repair |
|---|---|
| Empty `APIC` or `GEOB` frame (error: *A picture frame must contain at least 5 bytes*) | Remove the frame |
| Bad version byte such as `0x23` (error: *Unsupported tag version*), when the frames parse cleanly as v2.3 or v2.4 | Correct the version byte |
| Stated tag size runs past the padding into the first MPEG frame | Correct the stated size |

Each repair is verified in memory before anything is written. When applied, the file is replaced atomically, its modified time is kept, and the original is saved as `<name>.mp3.bak`. An existing `.bak` is never overwritten.

Files with any other fault are left alone and listed as `Error`.

## Filling in missing albums

A missing Album tag (empty or only whitespace) is set to the name of the folder directly containing the file. A file directly in the chosen folder therefore gets that folder's name. A file that is repaired in the same run can also get an album.

Files with `Error` or `Warning` status are skipped, so tags are never rewritten on a file that may be damaged or not really an mp3.

All tag changes are saved to a temporary copy that is then swapped in, so a failed save can't damage the original. The modified time is kept, but no `.bak` is made.

## Correcting æ, ø and å

Some rippers stored the letters as UTF-8 but marked them as Latin-1, so a player shows each one as two characters: `Ã¦` for æ, `Ã¸` for ø and `Ã¥` for å. **Fix æ, ø, å** corrects them in Title, Artist, Album, Album Artist and the file name. Other letters with the same fault are corrected too, such as ä, ö, é and ´.

In the capital letters Æ, Ø and Å (and Ä, Ö) the second character is often lost or turned into a space, as in `Mine Ã jne`. The letter is then looked up in the file name, so `Mine Ã jne` becomes `Mine Øjne` when the file is called `Mine Øjne De Skal Se.mp3`. If neither the file name nor the tags tell which letter it was, the text is left as it is and the file gets a note in the `Errors` column.

A file name is checked against the corrected tags. Where they spell a word differently, the tag wins, so `VÃœr vor gÃœst.mp3` is renamed to `Vær vor gæst.mp3` when that is the title. A file is never renamed over an existing file.

## Splitting "Artist / Title"

Many compilations put the artist in the title, as in `George Michael / Careless Whisper`, and set Artist to "Various". **Split titles** moves the artist to the Artist tag and leaves only the song in Title.

| Choice | Effect |
|---|---|
| **Off** | Titles are left as they are |
| **Automatic, per folder** | Works out for each folder whether the artist comes first or last. See below. |
| **Artist / Title** | The artist always comes first |
| **Title / Artist** | The artist always comes last |

A title is split at ` / `, or at a bare `/` when the title has no ` - ` (`Razz/Kickflipper`). In `Kasper Winding/Lars Muhl - Sjæl I Flammer` the `/` is part of the artist, so the title is left alone.

**Automatic** looks at every title in the scan for clues to which half is the artist:
- The half is a known artist: it's an Artist tag elsewhere, or the first part of an "Artist - Title" title.
- The same half appears in more than one title.
- The half reads like a Eurovision entry: `Charlotte Nilsson, Sverige - 1999`.
- The half joins names with `&`, `feat.` or a comma.

A half in parentheses or ending in `?` or `!` counts as the song. If the clues are even, the artist is taken to come first, which is the more common order. Check the Artist and Title columns after a scan, because a folder that mixes both orders gets some of them wrong.

## CSV log

Every run writes `mp3-log-YYYYMMDD-HHMMSS.csv` to the top of the chosen folder, scans included. It has one row per mp3.

- **Format:** RFC 4180, comma-separated, UTF-8 with BOM.
- **Values:** numbers are whole and dates are `yyyy-MM-dd HH:mm:ss`, so they don't depend on locale. A missing value is an empty cell.
- **Columns:** `Status`, `Errors`, `Changes`, `RelativePath`, `Directory`, `FileName`, `FullPath`, `SizeBytes`, `Created`, `Modified`, `Title`, `Artist`, `AlbumArtist`, `Album`, `Genre`, `Composer`, `Comment`, `Year`, `Track`, `TrackCount`, `Disc`, `Duration`, `DurationMs`, `BitrateKbps`, `SampleRateHz`, `Channels`, `TagTypes`, `PictureCount`.

The `Changes` column records which files were changed, or would be by a scan.

---

## For developers

| Project | What it is |
|---|---|
| `Mp3TagUpdater.Core` | The DLL with all the logic: `new Mp3Library().Run(options, progress, cancellation)` and `Mp3CsvLog.Write` |
| `Mp3TagUpdater.Cli` | Command line, `Mp3TagUpdater.exe` |
| `Mp3TagUpdater.Wpf` | Windows GUI, `Mp3TagUpdaterWpf.exe` |
| `Mp3TagUpdater.Tests` | xUnit tests, which generate their own mp3 files |

The app uses [TagLibSharp](https://github.com/mono/taglib-sharp) to read and write tags. Installs and updates use [Velopack](https://velopack.io).

```
Mp3TagUpdater.Core/
├── Mp3Run.cs          # run options, progress, messages and result
├── Mp3TagRecord.cs    # immutable record of one file's details, tags and status
├── Mp3Library.cs      # scanning, reading, repairing and updating
├── MisencodedText.cs  # corrects UTF-8 letters read as Windows-1252 ("Ã¸" → "ø")
├── ArtistTitleSplit.cs # splits "Artist / Title" and works out the order per folder
├── Id3v2Repair.cs     # byte-level repair of damaged ID3v2 tags
└── Mp3CsvLog.cs       # CSV log writer
```

### Build and test

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build Mp3TagUpdater.slnx
```

```bash
dotnet test Mp3TagUpdater.Tests
```

### Command line

The installer only includes the Windows app. To use the command line, build `Mp3TagUpdater.Cli`, or run it from source:

```bash
dotnet run --project Mp3TagUpdater.Cli -- "C:\Music\Party"
```

```
Mp3TagUpdater <directory> [--apply] [--no-recursive] [--no-log]
              [--fix-letters] [--split-titles[=auto|artist-first|title-first]]
```

| Option | Effect |
|---|---|
| `--apply` | Write repairs, tags and new file names to disk. Without it, changes are only proposed. |
| `--no-recursive` | Scan only the top-level directory, not subfolders. |
| `--no-log` | Don't write the CSV log. |
| `--fix-letters` | Correct misencoded letters such as `Ã¸` in tags and file names. |
| `--split-titles` | Split "Artist / Title" titles, with the order worked out per folder. Add `=artist-first` or `=title-first` to fix the order. |
| `-h`, `--help` | Show help. |

The console lists only proposed or actual changes, followed by a summary. Ctrl+C stops after the current file.

```
  [DRY RUN] Would repair Damaged song.mp3: Corrected ID3v2 version byte 0x23 to 0x03
  [DRY RUN] Would set Album to "Party": No album.mp3
Summary: OK 1, Repairable 1, Updatable 1
No files were modified. Pass --apply to write these changes.
Log written to C:\Music\Party\mp3-log-20261105-110130.csv
```

| Exit code | Meaning |
|---|---|
| `0` | The run finished |
| `1` | Invalid arguments, a missing directory, or the log couldn't be written |
| `130` | Cancelled |

### Releases

Every push to `main` runs the tests, builds the app on GitHub Actions and publishes a new release, one patch version higher than the last (`.github/workflows/release.yml`). To push without releasing, add `[skip ci]` to the commit message. You can also start a release by hand from the repository's Actions tab, or run `Mp3TagUpdater.Wpf\BuildRelease.ps1` locally. Add `-LocalOnly` to build without uploading.

The repository must stay public, because **Check for updates** reads its releases without logging in.

## License & Authorship

This project is distributed under the terms of the [MIT License](LICENSE).

**AI authorship note:** Most of the source code in this repository was generated with AI coding agents (Claude Code), under my direction and review. Purely AI-generated material may not be eligible for copyright protection in some jurisdictions. To the extent any part of this project is protected by copyright, it is licensed under the MIT License, and the license's warranty and liability disclaimers apply to the project as a whole.

<!-- notices:start -->
### Independent development

Mp3TagUpdater is an independent, personal project developed by Per Hejndorf. It is not affiliated with, endorsed by or sponsored by Microsoft or any other rights holder. Product names and logos are trademarks of their respective owners and are used only to identify the services and tools the project works with.

### Third-party notices and asset provenance

[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) lists every third-party library, font and online service the project uses, with version, licence, source and whether it was modified. It also records where the icons, images, audio and video came from and how they are licensed.
<!-- notices:end -->
