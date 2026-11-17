using Mp3TagUpdater.Models;
using Mp3TagUpdater.Services;

namespace Mp3TagUpdater.Cli;

public static class Program
{
    private static readonly string[] KnownOptions = ["--apply", "--dry-run", "--no-recursive", "--no-log"];

    public static int Main(string[] args)
    {
        // --- Parse arguments -------------------------------------------------
        // Usage: Mp3TagUpdater <directory> [--apply] [--no-recursive] [--no-log]
        //
        // Dry-run is the DEFAULT behavior: no mp3 files are modified unless
        // --apply is explicitly passed. --dry-run is also accepted for
        // clarity/backwards compatibility but has no extra effect.

        if (args.Contains("-h") || args.Contains("--help"))
        {
            PrintUsage();
            return 0;
        }

        // Options are rejected if misspelled, because a run with --apply writes
        // to files and shouldn't silently ignore something like --no-recursve.
        var unknownOption = args.FirstOrDefault(a => a.StartsWith('-') && !KnownOptions.Contains(a));
        var directories = args.Where(a => !a.StartsWith('-')).ToList();

        if (unknownOption is not null || directories.Count != 1)
        {
            if (unknownOption is not null)
                WriteError($"Unknown option: {unknownOption}");
            else if (directories.Count > 1)
                WriteError($"Expected one directory, but got {directories.Count}. Put quotes around a path that contains spaces.");

            PrintUsage();
            return 1;
        }

        string directory = directories[0];
        bool dryRun = !args.Contains("--apply");
        bool recursive = !args.Contains("--no-recursive");
        bool writeLog = !args.Contains("--no-log");

        if (!Directory.Exists(directory))
        {
            WriteError($"Directory does not exist: {directory}");
            return 1;
        }

        // Changes and failures are printed as they happen; everything else goes in the log.
        var library = new Mp3Library(message =>
            WriteColored(message.Kind == Mp3MessageKind.Error ? ConsoleColor.Red : ConsoleColor.Yellow, "  " + message.Text));

        // Ctrl+C stops after the current file, so no file is left half written.
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        // --- Read every mp3, repair unreadable files, fill in missing albums --
        var result = library.Run(new Mp3RunOptions(directory) { DryRun = dryRun, Recursive = recursive }, cancellation: cancellation.Token);
        var records = result.Records;

        if (records.Count == 0)
        {
            Console.WriteLine(result.Cancelled ? "Cancelled." : "No .mp3 files found.");
            return result.Cancelled ? 130 : 0;
        }

        Console.WriteLine("Summary: " + result.Summary);

        if (result.Cancelled)
            WriteError("Cancelled before every file was handled.");

        if (dryRun && result.HasChanges)
            Console.WriteLine("No files were modified. Pass --apply to write these changes.");

        // --- Write the CSV log -----------------------------------------------
        if (writeLog)
        {
            try
            {
                Console.WriteLine($"Log written to {Mp3CsvLog.Write(records, directory)}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                WriteError($"Failed to write log: {ex.Message}");
                return 1;
            }
        }

        return result.Cancelled ? 130 : 0;
    }

    private static void WriteError(string message) => WriteColored(ConsoleColor.Red, message);

    private static void WriteColored(ConsoleColor color, string message)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Reads mp3 tags from a directory, repairs tag faults that make files");
        Console.WriteLine("unreadable, and fills in missing Album tags from the folder name.");
        Console.WriteLine("The console lists changes only. Every file's details, tags and errors");
        Console.WriteLine("go in a CSV log.");
        Console.WriteLine();
        Console.WriteLine("By default this is a DRY RUN: no mp3 files are modified. Pass --apply");
        Console.WriteLine("to write the changes.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Mp3TagUpdater <directory> [--apply] [--no-recursive] [--no-log]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --apply          Write changes to disk (omit this to preview only)");
        Console.WriteLine("  --no-recursive   Only scan the top-level directory, not subfolders");
        Console.WriteLine("  --no-log         Don't write mp3-log-YYYYMMDD-HHMMSS.csv to <directory>");
        Console.WriteLine("  -h, --help       Show this help message");
    }
}
