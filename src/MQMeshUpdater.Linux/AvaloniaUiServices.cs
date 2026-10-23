using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MQMeshUpdater.ViewModels;

namespace MQMeshUpdater.Linux;

public sealed class AvaloniaUiServices(Window owner) : IUiServices
{
    public Task<ReplaceCustomAnswer> AskReplaceCustomAsync(string title, string message) =>
        ShowDialogAsync(title, message, ReplaceCustomAnswer.Cancel, null,
            ("Replace them", ReplaceCustomAnswer.Replace, false),
            ("Skip them, update the rest", ReplaceCustomAnswer.Skip, true),
            ("Cancel", ReplaceCustomAnswer.Cancel, false));

    public async Task<string?> PickFolderAsync(string title, string? initialFolder)
    {
        if (owner.StorageProvider.CanPickFolder)
        {
            try
            {
                var start = initialFolder is null ? null : await owner.StorageProvider.TryGetFolderFromPathAsync(initialFolder);
                var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = title,
                    SuggestedStartLocation = start,
                    AllowMultiple = false,
                });

                return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // No portal or GTK to show a picker: ask for the path instead.
            }
        }

        return await AskForPathAsync(title, initialFolder);
    }

    public Task ShowMessageAsync(string title, string message) => ShowDialogAsync(title, message, true, null, ("OK", true, true));

    public void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { target }, UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _ = ShowMessageAsync("Could not open", $"{target}\n\nxdg-open is not available: {ex.Message}");
        }
    }

    private async Task<string?> AskForPathAsync(string title, string? initialFolder)
    {
        var box = new TextBox { Text = initialFolder ?? "", MinWidth = 460, Watermark = "/path/to/MacroQuest" };
        var ok = await ShowDialogAsync(title, "Type the folder's full path:", false, box, ("OK", true, true), ("Cancel", false, false));
        var path = box.Text?.Trim();

        if (!ok || string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (!Directory.Exists(path))
        {
            await ShowMessageAsync(title, $"Folder not found: {path}");
            return null;
        }

        return Path.GetFullPath(path);
    }

    /// <summary>
    /// A small modal dialog with a message, an optional extra control and a
    /// row of buttons. Closing it from the title bar gives <paramref name="closed"/>.
    /// </summary>
    private async Task<T> ShowDialogAsync<T>(string title, string message, T closed, Control? extra, params (string Label, T Value, bool IsDefault)[] buttons)
    {
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 640,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = owner.Icon,
        };

        var result = closed;
        Button? defaultButton = null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 18, 0, 0) };

        foreach (var (label, value, isDefault) in buttons)
        {
            var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };

            if (isDefault)
            {
                button.Classes.Add("accent");
                defaultButton = button;
            }

            button.Click += (_, _) =>
            {
                result = value;
                dialog.Close();
            };
            row.Children.Add(button);
        }

        var body = new StackPanel { Margin = new(22, 18) };
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 590 });

        if (extra is not null)
        {
            extra.Margin = new(0, 10, 0, 0);
            body.Children.Add(extra);
        }

        body.Children.Add(row);
        dialog.Content = body;

        // Enter picks the default button and Escape closes, even before the
        // window manager gives the dialog focus.
        dialog.Opened += (_, _) => ((Control?)extra ?? defaultButton)?.Focus();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };

        await dialog.ShowDialog(owner);

        return result;
    }
}
