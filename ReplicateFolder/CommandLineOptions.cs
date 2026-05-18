using CommandLine;

namespace ReplicateFolder;

public sealed class CommandLineOptions
{
    [Option('s', "source", HelpText = "Source folder (read-only).")]
    public string SourcePath { get; set; } = "";

    [Option('r', "replica", HelpText = "Replica folder; will be modified to mirror source.")]
    public string ReplicaPath { get; set; } = "";

    [Option('i', "interval", HelpText = "Synchronization interval, in seconds (>0).")]
    public int IntervalSeconds { get; set; }

    [Option('l', "log", HelpText = "Path to log file (created/appended).")]
    public string LogPath { get; set; } = "";

    [Option("once", HelpText = "Run a single sync pass and exit.")]
    public bool RunOnce { get; set; }

    [Option('h', "help", HelpText = "Show this help.")]
    public bool ShowHelp { get; set; }

    public static bool TryParse(string[] args, out CommandLineOptions options, out string error)
    {
        var parsedOptions = new CommandLineOptions();
        var parseError = "";

        var parserResult = Parser.Default.ParseArguments<CommandLineOptions>(args);
        var parsed = false;

        parserResult
            .WithParsed(o =>
            {
                parsedOptions = o;
                parsed = true;
            })
            .WithNotParsed(errs =>
            {
                if (errs.Any(e => e.Tag is ErrorType.HelpRequestedError or ErrorType.HelpVerbRequestedError))
                {
                    parsedOptions = new CommandLineOptions { ShowHelp = true };
                    parsed = true;
                    return;
                }

                parseError = BuildParserError(errs);
            });

        if (!parsed)
        {
            options = parsedOptions;
            error = parseError;
            return false;
        }

        if (parsedOptions.ShowHelp)
        {
            options = parsedOptions;
            error = "";
            return true;
        }

        if (string.IsNullOrWhiteSpace(parsedOptions.SourcePath))   { options = parsedOptions; error = "--source is required"; return false; }
        if (string.IsNullOrWhiteSpace(parsedOptions.ReplicaPath))  { options = parsedOptions; error = "--replica is required"; return false; }
        if (parsedOptions.IntervalSeconds <= 0)                    { options = parsedOptions; error = "--interval must be a positive integer (seconds)"; return false; }
        if (string.IsNullOrWhiteSpace(parsedOptions.LogPath))      { options = parsedOptions; error = "--log is required"; return false; }

        parsedOptions.SourcePath = NormalizePath(parsedOptions.SourcePath);
        parsedOptions.ReplicaPath = NormalizePath(parsedOptions.ReplicaPath);
        parsedOptions.LogPath = Path.GetFullPath(parsedOptions.LogPath);

        if (PathsOverlap(parsedOptions.SourcePath, parsedOptions.ReplicaPath))
        {
            options = parsedOptions;
            error = "Source and replica paths must not be the same or nested in each other.";
            return false;
        }

        options = parsedOptions;
        error = "";
        return true;
    }

    private static string BuildParserError(IEnumerable<Error> errors)
    {
        var first = errors.FirstOrDefault();
        if (first is null) return "Invalid arguments. Use --help for usage.";

        return first.Tag switch
        {
            ErrorType.UnknownOptionError => "Unknown argument. Use --help for usage.",
            ErrorType.MissingValueOptionError => "An option is missing its value. Use --help for usage.",
            ErrorType.BadFormatConversionError => "Invalid option value format. Use --help for usage.",
            _ => "Invalid arguments. Use --help for usage."
        };
    }

    private static string NormalizePath(string p)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));

    private static bool PathsOverlap(string a, string b)
    {
        var cmp = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var sep = Path.DirectorySeparatorChar;

        if (a.Equals(b, cmp)) return true;
        if (a.StartsWith(b + sep, cmp)) return true;
        if (b.StartsWith(a + sep, cmp)) return true;
        return false;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("FolderSync - one-way periodic folder synchronization");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  FolderSync --source <path> --replica <path> --interval <seconds> --log <path> [--once]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -s, --source    <path>     Source folder (read-only).");
        Console.WriteLine("  -r, --replica   <path>     Replica folder; will be modified to mirror source.");
        Console.WriteLine("  -i, --interval  <seconds>  Synchronization interval, in seconds (>0).");
        Console.WriteLine("  -l, --log       <path>     Path to log file (created/appended).");
        Console.WriteLine("      --once                 Run a single sync pass and exit.");
        Console.WriteLine("  -h, --help                 Show this help.");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  FolderSync --source ./data --replica ./backup --interval 30 --log ./sync.log");
    }
}
