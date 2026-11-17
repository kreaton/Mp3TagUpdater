# Mp3TagUpdater

[![Release](https://img.shields.io/github/v/release/kreaton/Mp3TagUpdater)](https://github.com/kreaton/Mp3TagUpdater/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
![Windows](https://img.shields.io/badge/platform-Windows-lightgrey)
![.NET 10](https://img.shields.io/badge/.NET-10-lightgrey)
![WPF](https://img.shields.io/badge/UI-WPF-lightgrey)

![Mp3TagUpdater: repair broken mp3 tags and fill in missing albums from the folder name](images/banner.png)

**Fix the mp3 files your player can't read, and give every song an album.**

Mp3TagUpdater checks a folder of `.mp3` files, repairs tag faults that make files unreadable, and fills in missing Album tags from the folder name. It can also correct Danish letters that show as two characters ("Ã¸" for "ø"), read missing titles, artists and track numbers from file names such as "1-02 Artist - Title.mp3", and move the artist out of titles such as "Artist / Title".

- **Safe by default:** a scan only shows what would change. Nothing is written until you choose **Apply**.
- **Every file at a glance:** status, album, track, artist, title, length, and what would change or went wrong.
- **Full CSV log:** every file's details, tags and errors in a log that opens in a spreadsheet or loads into a database, including files that couldn't be read.

## Install

**[Download the installer](https://github.com/kreaton/Mp3TagUpdater/releases/latest/download/Mp3TagUpdater-win-Setup.exe)** and run it. If your PC doesn't have the .NET 10 Desktop Runtime yet, the installer adds it.

The installer isn't code-signed, so Windows may warn you about it. To continue, choose **More info → Run anyway**.

To get a newer version later, click the ⓘ button and choose **Check for updates**. To remove the app, use **Settings → Apps → Installed apps** in Windows.

## How to use it

1. Choose a **Folder** of mp3 files with **Browse...**, or drop a folder on the window.
2. Choose **Scan**. Every mp3 is read and listed, and nothing is changed. A changed album is shown as `→ Folder name`.
3. Check the list. **Show** narrows it to the files with changes, to only the `Updatable` files, or to errors and warnings. **Hide OK** leaves out the files with nothing to change and no problems, and **Search** finds a file by path, title, artist or album.
4. Choose **Apply N changes...** and confirm. The folder is read again and the changes are written.

**Cancel** stops after the current file. Double-click a file to open it in its default program, so an mp3 plays in your usual player. Drag files from the list into another program or a folder, as from Explorer; several selected files drag together, and they are copied, never moved. Right-click to show a file in Explorer or copy its path.

Untick **Include subfolders** to check only the top-level folder. Untick **Write CSV log** to skip the log. **Open log** opens the log of the last run. Tick **Fix æ, ø, å**, **Fix all file and folder names**, **Tags from file names** or **Album from folder name**, and choose **Split titles**, to use the options described below. A new value is shown as `→ value` in the list until it is applied.

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

Tick **Album from folder name** to replace albums that are already set as well, for example after renaming a folder. Every file then gets the name of its folder as Album, and files whose album already matches are left alone.

Files with `Error` or `Warning` status are skipped, so tags are never rewritten on a file that may be damaged or not really an mp3.

All tag changes are saved to a temporary copy that is then swapped in, so a failed save can't damage the original. The modified time is kept, but no `.bak` is made.

## Tags from file names

**Tags from file names** fills in an empty Title, Artist, Track and Disc from the file name. Tags that are already set are never changed. These names are understood:

| File name | Disc | Track | Artist | Title |
|---|---|---|---|---|
| `1-02 Juan Maglio - Union Civica.mp3` | 1 | 2 | Juan Maglio | Union Civica |
| `02 Kim Larsen - Papirsklip.mp3`, `02 - Kim Larsen - Papirsklip.mp3` | | 2 | Kim Larsen | Papirsklip |
| `7. Papirsklip.mp3`, `12-Papirsklip.mp3` | | 7, 12 | | Papirsklip |
| `Kim Larsen - Papirsklip.mp3` | | | Kim Larsen | Papirsklip |
| `Papirsklip.mp3` | | | | Papirsklip |

The name is split at the first ` - `, so the artist comes first and a title may contain ` - ` itself. A name with the title first is read the wrong way round, so check the Artist and Title columns before applying.

A number followed only by a space, such as `50 Cent - In da Club`, counts as a track number only when most files in the folder start with a number. A leading zero (`05 `) or a `.` or `-` after the number always makes it a track number. A four-digit number such as `1999` is never a track number.

With **Fix æ, ø, å** or **Fix all file and folder names** ticked, the letters in the file name are corrected before it is read.

## Correcting æ, ø and å

Some rippers stored the letters as UTF-8 but marked them as Latin-1, so a player shows each one as two characters: `Ã¦` for æ, `Ã¸` for ø and `Ã¥` for å. **Fix æ, ø, å** corrects them in Title, Artist, Album, Album Artist and the file name. Other letters with the same fault are corrected too, such as ä, ö, é and ´.

In the capital letters Æ, Ø and Å (and Ä, Ö) the second character is often lost or turned into a space, as in `Mine Ã jne`. The letter is then looked up in the file name, so `Mine Ã jne` becomes `Mine Øjne` when the file is called `Mine Øjne De Skal Se.mp3`. If neither the file name nor the tags tell which letter it was, the text is left as it is and the file gets a note in the `Errors` column.

A file name is checked against the corrected tags. Where they spell a word differently, the tag wins, so `VÃœr vor gÃœst.mp3` is renamed to `Vær vor gæst.mp3` when that is the title. A file is never renamed over an existing file.

## Fixing all file and folder names

**Fix all file and folder names** goes further than the mp3 files: it renames every file and folder below the chosen folder whose name has garbled letters, such as covers, playlists and the album folders themselves. The chosen folder keeps its own name. It uses the same rules as the MojibakeFileFix command-line tool and recognises more kinds of damage than **Fix æ, ø, å**:

| Damaged name | Fixed | Letters were read as |
|---|---|---|
| `SÃ¸ren Ã…rhus` | `Søren Århus` | Windows-1252 |
| `Donâ€™t â€“ stop` | `Don’t – stop` | Windows-1252 |
| `ÃƒÂ¸l` (garbled twice) | `øl` | Windows-1252 |
| `├ª├╕├Ñ` | `æøå` | DOS 437 |
| `S├©ren` | `Søren` | DOS 850, common in files unpacked from zip archives |

Only characters that together form a real letter are changed, so correct names such as `Århus`, `São Paulo` or `CHÂTEAU` are left alone. A missing Album is filled in with the corrected folder name, so an mp3 in `SÃ¸ren` gets the album `Søren`.

Some names are listed as `Warning` and not renamed:
- **Uncertain:** the only sign is one pair of characters that could also be correct, as in `MaÅ‚y` (Polish "Mały").
- **Lost letters:** a byte was lost, as in `Ã jne` for "Øjne", or a letter was replaced by `�`.

A folder's contents are renamed before the folder, and nothing is renamed over an existing file or folder. Folder links (junctions and symbolic links) are renamed but not entered. Files and folders that are renamed appear in the list and the log as their own rows, with a folder's name ending in `\`.

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

Every run writes `mp3-log-YYYYMMDD-HHMMSS.csv` to the top of the chosen folder, scans included. It has one row per mp3, plus a row for each other file or folder that **Fix all file and folder names** renames or warns about.

- **Format:** RFC 4180, comma-separated, UTF-8 with BOM.
- **Values:** numbers are whole and dates are `yyyy-MM-dd HH:mm:ss`, so they don't depend on locale. A missing value is an empty cell.
- **Columns:** `Status`, `Errors`, `Changes`, `RelativePath`, `Directory`, `FileName`, `FullPath`, `SizeBytes`, `Created`, `Modified`, `Title`, `Artist`, `AlbumArtist`, `Album`, `Genre`, `Composer`, `Comment`, `Year`, `Track`, `TrackCount`, `Disc`, `Duration`, `DurationMs`, `BitrateKbps`, `SampleRateHz`, `Channels`, `TagTypes`, `PictureCount`, `Kind`.
- **Kind:** `mp3`, `file` or `folder`. It is the last column, so a log from before it was added still lines up.

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
├── MojibakeText.cs    # corrects names read as Windows-1252, DOS 437 or 850, for all files and folders
├── CodePageMap.cs     # maps a code page's characters back to their bytes
├── ArtistTitleSplit.cs # splits "Artist / Title" and works out the order per folder
├── FileNameTags.cs    # reads disc, track, artist and title from file names
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
              [--fix-letters] [--fix-names] [--from-names] [--album-from-folder] [--split-titles[=auto|artist-first|title-first]]
```

| Option | Effect |
|---|---|
| `--apply` | Write repairs, tags and new file names to disk. Without it, changes are only proposed. |
| `--no-recursive` | Scan only the top-level directory, not subfolders. |
| `--no-log` | Don't write the CSV log. |
| `--fix-letters` | Correct misencoded letters such as `Ã¸` in tags and file names. |
| `--fix-names` | Correct misencoded letters in the names of all files and folders, not only mp3 files. |
| `--album-from-folder` | Set every Album to the folder name, replacing albums that are already set. |
| `--from-names` | Fill in a missing Title, Artist, Track and Disc from file names such as `1-02 Artist - Title.mp3`. |
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
