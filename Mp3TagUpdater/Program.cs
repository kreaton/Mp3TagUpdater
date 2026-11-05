using Mp3TagUpdater.Models;
using Mp3TagUpdater.Services;

namespace Mp3TagUpdater;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("=== MP3 Tag Updater ===");
        Console.WriteLine();

        // --- Parse arguments -------------------------------------------------
        // Usage: Mp3TagUpdater <directory> [--apply] [--no-recursive] [--no-log]
        //
        // Dry-run is the DEFAULT behavior: no mp3 files are modified unless
        // --apply is explicitly passed. --dry-run is also accepted for
        // clarity/backwards compatibility but has no extra effect.

        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        string directory = args[0];
        //string directory = @"c:\Users\perhe\Downloads\MP4Player\Audio\GermanMilitary\Musik\";
        bool apply = args.Contains("--apply");
        bool dryRun = !apply; // default true unless --apply is given
        bool recursive = !args.Contains("--no-recursive");
        bool writeLog = !args.Contains("--no-log");

        if (!Directory.Exists(directory))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Directory does not exist: {directory}");
            Console.ResetColor();
            return 1;
        }

        var library = new Mp3Library();

        // --- Step 1: read every mp3 file's tags into a list of records -------
        Console.WriteLine($"Scanning '{directory}' ({(recursive ? "recursive" : "top-level only")})...");
        var records = library.ReadTags(directory, recursive);

        if (records.Count == 0)
        {
            Console.WriteLine("No .mp3 files found.");
            return 0;
        }

        Console.WriteLine($"Found {records.Count} mp3 file(s):");

        records.ForEach(x => Console.WriteLine(x.FileName));

        // --- Repair files that couldn't be read ------------------------------
        if (records.Any(r => r.Status == Mp3Status.Error))
        {
            Console.WriteLine();
            Console.WriteLine(dryRun
                ? "Checking unreadable files (no files will be modified). Pass --apply to repair them:"
                : "Repairing unreadable files (--apply was specified)...");
            records = library.RepairUnreadableFiles(records, dryRun);
        }

        Console.WriteLine();
        Console.WriteLine("Summary: " + string.Join(", ",
            records.GroupBy(r => r.Status).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")));

        // --- Write the CSV log -----------------------------------------------
        if (writeLog)
        {
            try
            {
                Console.WriteLine($"Log written to {Mp3CsvLog.Write(records, directory)}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to write log: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        //foreach (var record in records)
        //    Console.WriteLine($"  {record}");

        //var missingCount = records.Count(r => r.AlbumWasMissing);
        //Console.WriteLine();
        //Console.WriteLine($"{missingCount} file(s) are missing an Album tag.");

        //if (missingCount == 0)
        //{
        //    Console.WriteLine("Nothing to update.");
        //    return 0;
        //}

        //// --- Step 2: update Album tag from directory name where missing ------
        //Console.WriteLine();
        //Console.WriteLine(dryRun
        //    ? "Performing dry run (no files will be modified). Pass --apply to write changes:"
        //    : "Applying changes (--apply was specified)...");
        //var updatedRecords = library.UpdateMissingAlbumTags(records, dryRun);

        //Console.WriteLine();
        //Console.WriteLine("Done. Final state:");
        //foreach (var record in updatedRecords)
        //    Console.WriteLine($"  {record}");

        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Reads mp3 tags from a directory, repairs tag faults that make files");
        Console.WriteLine("unreadable, and logs every file's details and errors to a CSV file.");
        Console.WriteLine();
        Console.WriteLine("By default this is a DRY RUN: no mp3 files are modified. Pass --apply");
        Console.WriteLine("to actually repair files. Each original is kept as <name>.mp3.bak.");
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
