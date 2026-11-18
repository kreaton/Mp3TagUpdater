# Mp3Tag.Text

Repairs **mojibake**: letters such as æ, ø and å that were stored as UTF-8 but read with a single-byte code page, so each one turns into two to four characters.

| Stored | Read as | Shown as |
|---|---|---|
| `Søren` | Windows-1252 | `SÃ¸ren` |
| `Søren` | DOS 437 / 850 | `S├©ren` |
| `Søren`, garbled twice | Windows-1252, twice | `SÃƒÂ¸ren` |

Correct text is never touched: only character sequences that decode to valid UTF-8 are repaired, so `Århus`, `São Paulo` and `CHÂTEAU` stay as they are.

It is a plain .NET 10 class library with no dependencies, used by `Mp3Tag.Core` (Mp3TagUpdater) and `Mp3Tag.Albums` (Mp3TagFromApi). All types are in the `Mp3Tag.Text` namespace.

## API

### `TextRepair`: the simple entry point

```csharp
string? TextRepair.Fix(string? text, params IEnumerable<string?> references)
string  TextRepair.FixOrKeep(string text, params IEnumerable<string?> references)
```

`Fix` returns the corrected text, or `null` when there is nothing to correct or the correction isn't certain. `FixOrKeep` returns the text unchanged instead of `null`. It combines the two classes below: `MisencodedText` first, which can recover a lost letter from the references, then `MojibakeText`, which also knows the DOS code pages and text garbled twice.

```csharp
TextRepair.Fix("SÃ¸ren")                                  // "Søren"
TextRepair.Fix("├ª├©├Ñ")                                   // "æøå"
TextRepair.Fix("Mine Ã jne", "18 Lis Sørensen - Mine Øjne") // "Mine Øjne": the lost letter is taken from the reference
TextRepair.Fix("Søren")                                   // null: nothing to fix
```

`references` are other texts that may spell the same words correctly, such as the file name for a title tag, or the folder name for an album tag.

### `MisencodedText`: Windows-1252 letters, with references

```csharp
MisencodedFix? MisencodedText.Fix(string? text, IEnumerable<string?> references, bool trustReferences = false)
record MisencodedFix(string Text, int Unresolved)
```

Repairs UTF-8 read as Windows-1252 (`Ã¦ Ã¸ Ã¥` → `æ ø å`). In the upper-case letters Æ, Ø, Å, Ä and Ö the second byte is a control code that some programs replaced with a space (`Ã jne` was `Øjne`). That letter can only come from another copy of the same word, so `Fix` looks the word up in `references`. `Unresolved` counts the letters it couldn't work out, which are left as they were.

With `trustReferences`, a reference also wins where it spells a word with a different letter. This helps with rippers that wrote one wrong letter throughout a file name (`vÃœre` for `være`), which the title tag gets right.

### `MojibakeText`: any of several code pages

```csharp
MojibakeRepair? MojibakeText.TryRepair(string text)
MojibakeRepair? MojibakeText.TryRepair(string text, IReadOnlyList<CodePageMap> codePages)
IReadOnlyList<string> MojibakeText.FindUnrecoverable(string text)

record MojibakeRepair(string Original, string Fixed, int CodePage, int Sequences, bool Certain)
```

`TryRepair` tries each code page (by default Windows-1252, DOS 437 and DOS 850) and keeps the one that decodes the most sequences. It decodes up to three times, for text that was garbled more than once.

`Certain` is false when the only evidence is one two-character sequence that could be ordinary text, such as `Å‚` (Polish `ł`). Apply those repairs only if the user asks.

`FindUnrecoverable` describes damage that can't be repaired because a byte was lost, such as a `�` replacement character or `Ã` followed by a space. Run it on the repaired text.

### `CodePageMap`

```csharp
CodePageMap CodePageMap.Get(int codePage)          // 1252, 437, 850, ... (single-byte code pages only)
bool        map.TryGetByte(char c, out byte value) // the byte 0x80–0xFF that c stands for
IReadOnlyList<int> CodePageMap.DefaultCodePages     // [1252, 437, 850]
```

Maps a code page's characters back to the bytes they were decoded from. The maps are cached. `Get` throws `ArgumentException` for an unknown or multi-byte code page.

## Tests

`Mp3Tag.Tests/MojibakeTextTests.cs`, `TextFixTests.cs` and `Albums/LookupTests.cs` (`TextRepairTests`).
