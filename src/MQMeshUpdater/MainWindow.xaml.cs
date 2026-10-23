using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using MQMeshUpdater.Core.Local;
using MQMeshUpdater.ViewModels;

namespace MQMeshUpdater;

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

        var zones = CollectionViewSource.GetDefaultView(viewModel.Rows);
        zones.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ZoneRowViewModel.Group)));
        zones.SortDescriptions.Add(new SortDescription("Group.Order", ListSortDirection.Ascending));
        zones.SortDescriptions.Add(new SortDescription(nameof(ZoneRowViewModel.Name), ListSortDirection.Ascending));
        zones.Filter = item => item is ZoneRowViewModel row && viewModel.Matches(row);
        viewModel.ViewChanged += (_, _) => zones.Refresh();
        ZonesGrid.ItemsSource = zones;

        RestorePlacement(viewModel.Engine.Settings.Window);

        InputBindings.Add(new KeyBinding(viewModel.RefreshCommand, Key.F5, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new FocusCommand(() => SearchBox.Focus()), Key.F, ModifierKeys.Control));

        Loaded += async (_, _) => await viewModel.RefreshCommand.ExecuteAsync(null);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _viewModel.Engine.Settings.Window = new WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
        _viewModel.SaveSettings();

        base.OnClosing(e);
    }

    private void RestorePlacement(WindowPlacement? placement)
    {
        if (placement is null || placement.Width < MinWidth || placement.Height < MinHeight)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        // Only restore a position that is still on screen.
        var onScreen = placement.Left >= SystemParameters.VirtualScreenLeft - 50
            && placement.Top >= SystemParameters.VirtualScreenTop - 50
            && placement.Left + 100 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && placement.Top + 100 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

        Width = placement.Width;
        Height = placement.Height;

        if (onScreen)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = placement.Left;
            Top = placement.Top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (placement.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private async void MeshCombo_DropDownOpened(object sender, EventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ZoneRowViewModel row })
        {
            await row.EnsureCommunityAsync();
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class FocusCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => action();
    }
}
