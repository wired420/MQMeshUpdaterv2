using System.Runtime.InteropServices;
using System.Windows;
using MQMeshUpdater.Core.Sync;
using MQMeshUpdater.ViewModels;

namespace MQMeshUpdater;

public partial class App : Application
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = CommandLineOptions.Parse(e.Args);

        if (options.Headless)
        {
            // A Windows app has no console of its own; borrow the one it was started from.
            AttachConsole(AttachParentProcess);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await UpdaterHost.RunHeadlessAsync(options));
            return;
        }

        var http = UpdaterHost.CreateHttpClient();
        var window = new MainWindow();
        window.Attach(new MainViewModel(UpdaterHost.CreateEngine(http), new WpfUiServices(window)));
        MainWindow = window;
        window.Show();
    }
}
