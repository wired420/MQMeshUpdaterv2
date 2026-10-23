using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MQMeshUpdater.Core.Sync;
using MQMeshUpdater.ViewModels;

namespace MQMeshUpdater.Linux;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            window.Attach(new MainViewModel(UpdaterHost.CreateEngine(UpdaterHost.CreateHttpClient()), new AvaloniaUiServices(window)));
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
