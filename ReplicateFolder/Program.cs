namespace ReplicateFolder;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!CommandLineOptions.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine($"Error: {error}");
            Console.Error.WriteLine();
            CommandLineOptions.PrintUsage();
            return 1;
        }

        if (options.ShowHelp)
        {
            CommandLineOptions.PrintUsage();
            return 0;
        }

        try
        {
            var logDir = Path.GetDirectoryName(options.LogPath);
            if (!string.IsNullOrEmpty(logDir))
                Directory.CreateDirectory(logDir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Cannot prepare log directory: {ex.Message}");
            return 1;
        }

        using var logger = new Logger(options.LogPath);

        if (!Directory.Exists(options.SourcePath))
        {
            logger.LogError($"Source folder does not exist: {options.SourcePath}");
            return 1;
        }

        var synchronizer = new Synchronizer(options.SourcePath, options.ReplicaPath, logger);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            logger.Log("Cancellation requested. Stopping after current iteration...");
            cts.Cancel();
        };

        logger.Log("Starting folder sync.");
        logger.Log($"  Source:   {options.SourcePath}");
        logger.Log($"  Replica:  {options.ReplicaPath}");
        logger.Log($"  Interval: {options.IntervalSeconds}s");
        logger.Log($"  Log file: {options.LogPath}");
        if (options.RunOnce) logger.Log("  Mode:     run once");

        try
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    synchronizer.SyncOnce(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError($"Sync iteration failed: {ex.Message}");
                }

                if (options.RunOnce) break;

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            logger.Log("Folder sync stopped.");
        }

        return 0;
    }
}
