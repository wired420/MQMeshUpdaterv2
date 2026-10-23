using MQMeshUpdater.Core.Local;

namespace MQMeshUpdater.Core.Sync;

public sealed class CommandLineOptions
{
    public bool Update { get; init; }

    public bool Help { get; init; }

    public bool Quiet { get; init; }

    /// <summary>Also replace local files that match no known version.</summary>
    public bool Force { get; init; }

    public string? MqFolder { get; init; }

    public string? Error { get; init; }

    /// <summary>Whether to run without the window.</summary>
    public bool Headless => Update || Help || Error is not null;

    public const string Usage = """
        MQMesh Updater

        Usage:
          MQMeshUpdater.exe                    Open the updater window.
          MQMeshUpdater.exe --update [options] Apply your saved choices without a window.

        Options:
          --mq <folder>  MacroQuest folder (default: the saved one, else the exe's folder).
          --quiet        Only print errors.
          --force        Also replace local files that match no known version.
          --help         Show this help.

        Exit codes: 0 success, 1 some files failed, 2 could not run.
        """;

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        bool update = false, help = false, quiet = false, force = false;
        string? mq = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--update" or "-u" or "/update":
                    update = true;
                    break;
                case "--quiet" or "-q" or "/quiet":
                    quiet = true;
                    break;
                case "--force" or "/force":
                    force = true;
                    break;
                case "--help" or "-h" or "/?" or "/help":
                    help = true;
                    break;
                case "--mq" or "/mq":
                    if (i + 1 >= args.Count)
                    {
                        return new CommandLineOptions { Error = "--mq needs a folder." };
                    }

                    mq = args[++i];
                    break;
                default:
                    return new CommandLineOptions { Error = $"Unknown option {args[i]}." };
            }
        }

        return new CommandLineOptions { Update = update, Help = help, Quiet = quiet, Force = force, MqFolder = mq };
    }
}

/// <summary>
/// Runs an update with no window, for scheduled tasks.
/// </summary>
public static class CommandLineRunner
{
    public const int Success = 0;
    public const int SomeFailed = 1;
    public const int CouldNotRun = 2;

    public static async Task<int> RunAsync(CommandLineOptions options, SyncEngine engine, TextWriter output, TextWriter errors, CancellationToken cancellationToken = default)
    {
        if (options.Error is not null)
        {
            await errors.WriteLineAsync(options.Error + Environment.NewLine + Environment.NewLine + CommandLineOptions.Usage).ConfigureAwait(false);
            return CouldNotRun;
        }

        if (options.Help)
        {
            await output.WriteLineAsync(CommandLineOptions.Usage).ConfigureAwait(false);
            return Success;
        }

        void Say(string line)
        {
            if (!options.Quiet)
            {
                output.WriteLine(line);
            }
        }

        if (options.MqFolder is not null)
        {
            if (!Directory.Exists(options.MqFolder))
            {
                await errors.WriteLineAsync($"MacroQuest folder not found: {options.MqFolder}").ConfigureAwait(false);
                return CouldNotRun;
            }

            engine.Settings.MqFolder = Path.GetFullPath(options.MqFolder);
        }

        try
        {
            var catalog = await engine.LoadCatalogAsync(cancellationToken).ConfigureAwait(false);

            if (catalog.Warning is not null)
            {
                await errors.WriteLineAsync(catalog.Warning).ConfigureAwait(false);
            }

            Say($"Mesh folder: {engine.MeshFolder}");
            Say($"{catalog.Manifest.Zones.Count} zones on mqmesh.com. Checking local files...");
            await engine.ScanAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await errors.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return CouldNotRun;
        }

        var plans = engine.PlanAll();
        var custom = plans.Where(p => p.Status == ZoneStatus.Custom).ToList();
        var jobs = SyncEngine.JobsFor(plans, options.Force);

        if (custom.Count > 0 && !options.Force)
        {
            Say($"Skipping {custom.Count} zone(s) with local files that match no known version (use --force to replace): "
                + string.Join(", ", custom.Select(p => p.Zone.ShortName).Take(20)) + (custom.Count > 20 ? ", ..." : ""));
        }

        if (jobs.Count == 0)
        {
            Say(custom.Count > 0 && !options.Force ? "Nothing else to update." : "Everything is up to date.");
            return Success;
        }

        Say($"Downloading {jobs.Count} file(s), {Bytes.Format(jobs.Sum(j => j.File.Size))}...");

        var progress = new SyncProgress<DownloadProgress>(p =>
        {
            if (p.State == JobState.Done)
            {
                Say($"  [{p.Completed + p.Failed}/{p.Total}] {p.Job.File.FileName}");
            }
            else if (p.State == JobState.Failed)
            {
                errors.WriteLine($"  FAILED {p.Job.File.FileName}: {p.Error}");
            }
        });

        var report = await engine.DownloadAsync(jobs, progress, cancellationToken).ConfigureAwait(false);

        Say($"Done: {report.Succeeded} downloaded, {report.Failed} failed.");

        return report.Failed > 0 || report.Cancelled ? SomeFailed : Success;
    }
}

public static class Bytes
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>A size for display, such as 5.5 MB.</summary>
    public static string Format(long bytes)
    {
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {Units[unit]}";
    }
}

/// <summary>
/// Reports progress on the calling thread, in order, unlike <see cref="Progress{T}"/>.
/// </summary>
public sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly Lock _lock = new();

    public void Report(T value)
    {
        lock (_lock)
        {
            handler(value);
        }
    }
}
