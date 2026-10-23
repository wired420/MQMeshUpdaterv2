using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using MQMeshUpdater.ViewModels;

namespace MQMeshUpdater;

public sealed class WpfUiServices(Window owner) : IUiServices
{
    public Task<ReplaceCustomAnswer> AskReplaceCustomAsync(string title, string message)
    {
        var answer = MessageBox.Show(
            owner,
            message + "\n\nYes: replace them.  No: skip them, update the rest.  Cancel: do nothing.",
            title,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return Task.FromResult(answer switch
        {
            MessageBoxResult.Yes => ReplaceCustomAnswer.Replace,
            MessageBoxResult.No => ReplaceCustomAnswer.Skip,
            _ => ReplaceCustomAnswer.Cancel,
        });
    }

    public Task<string?> PickFolderAsync(string title, string? initialFolder)
    {
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory = initialFolder };

        return Task.FromResult(dialog.ShowDialog(owner) == true ? dialog.FolderName : null);
    }

    public Task ShowMessageAsync(string title, string message)
    {
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    public void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
