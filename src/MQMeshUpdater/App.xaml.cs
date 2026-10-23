using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using MQMeshUpdater.Core.Api;
using MQMeshUpdater.Core.Local;
using MQMeshUpdater.Core.Sync;
using MQMeshUpdater.ViewModels;

namespace MQMeshUpdater;

public partial class App : Application
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    /// <summary>
    /// The folder the exe is in, which is the default MacroQuest folder.
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
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MQMeshUpdater/{version} (+https://github.com/wired420/MQMeshUpdaterv2)");

        return http;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = CommandLineOptions.Parse(e.Args);
        var http = CreateHttpClient();
        var engine = new SyncEngine(new AppPaths(), http, new MeshApiClient(http), ExeFolder);

        if (options.Headless)
        {
            // A Windows app has no console of its own; borrow the one it was started from.
            AttachConsole(AttachParentProcess);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var exitCode = await CommandLineRunner.RunAsync(options, engine, Console.Out, Console.Error);
            Console.Out.Flush();
            Shutdown(exitCode);
            return;
        }

        var window = new MainWindow(new MainViewModel(engine));
        MainWindow = window;
        window.Show();
    }
}
