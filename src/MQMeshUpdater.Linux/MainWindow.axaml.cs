using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MQMeshUpdater.Core.Local;
using MQMeshUpdater.ViewModels;

namespace MQMeshUpdater.Linux;

public partial class MainWindow : Window
{
    private MainViewModel _viewModel = null!;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void Attach(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        RestorePlacement(viewModel.Engine.Settings.Window);

        Opened += async (_, _) => await viewModel.RefreshCommand.ExecuteAsync(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        var previous = _viewModel.Engine.Settings.Window;

        // A maximised window's size says nothing about where it was before, so keep the last normal placement.
        _viewModel.Engine.Settings.Window = maximized && previous is not null
            ? new WindowPlacement { Left = previous.Left, Top = previous.Top, Width = previous.Width, Height = previous.Height, Maximized = true }
            : new WindowPlacement { Left = Position.X, Top = Position.Y, Width = Width, Height = Height, Maximized = maximized };
        _viewModel.SaveSettings();

        base.OnClosing(e);
    }

    private void RestorePlacement(WindowPlacement? placement)
    {
        if (placement is null || placement.Width < MinWidth || placement.Height < MinHeight)
        {
            return;
        }

        Width = placement.Width;
        Height = placement.Height;

        // Only restore a position that is still on a screen.
        var position = new PixelPoint((int)placement.Left, (int)placement.Top);

        if (Screens.All.Any(s => s.Bounds.Contains(position + new PixelPoint(100, 100))))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = position;
        }

        if (placement.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private async void MeshCombo_DropDownOpened(object? sender, EventArgs e)
    {
        if (sender is Control { DataContext: ZoneRowViewModel row })
        {
            await row.EnsureCommunityAsync();
        }
    }

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();
}
