using System.Net;
using System.Runtime.InteropServices;
using MQMeshUpdater.Core.Api;
using MQMeshUpdater.Core.Local;

namespace MQMeshUpdater.Core.Sync;

/// <summary>
/// The setup every front end shares: the HTTP client, the engine, and the
/// no-window update.
/// </summary>
public static class UpdaterHost
{
    public static string Version => typeof(UpdaterHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// The folder the program is in, which is the default MacroQuest folder.
    /// </summary>
    public static string ExeFolder => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    public static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 8,
        };

        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        var os = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : RuntimeInformation.OSDescription;
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MQMeshUpdater/{Version} ({os}; +https://github.com/wired420/MQMeshUpdaterv2)");

        return http;
    }

    public static SyncEngine CreateEngine(HttpClient http) => new(new AppPaths(), http, new MeshApiClient(http), ExeFolder);

    /// <summary>
    /// Run <c>--update</c> or <c>--help</c> on the console and return the exit
    /// code. Ctrl+C stops the downloads.
    /// </summary>
    public static async Task<int> RunHeadlessAsync(CommandLineOptions options)
    {
        using var http = CreateHttpClient();
        using var cancel = new CancellationTokenSource();

        void OnCancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cancel.Cancel();
        }

        Console.CancelKeyPress += OnCancel;

        try
        {
            return await CommandLineRunner.RunAsync(options, CreateEngine(http), Console.Out, Console.Error, cancel.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Cancelled.").ConfigureAwait(false);
            return CommandLineRunner.SomeFailed;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
            Console.Out.Flush();
        }
    }
}
