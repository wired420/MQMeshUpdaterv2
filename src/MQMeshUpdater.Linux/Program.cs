using Avalonia;
using MQMeshUpdater.Core.Sync;

namespace MQMeshUpdater.Linux;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);

        if (options.Headless)
        {
            return UpdaterHost.RunHeadlessAsync(options).GetAwaiter().GetResult();
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex) when (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            Console.Error.WriteLine($"Could not open a window ({ex.Message}): no display was found.");
            Console.Error.WriteLine("Use --update to update without a window, or --help for options.");
            return CommandLineRunner.CouldNotRun;
        }
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
