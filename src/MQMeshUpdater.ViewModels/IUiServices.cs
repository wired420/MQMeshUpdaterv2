namespace MQMeshUpdater.ViewModels;

public enum ReplaceCustomAnswer
{
    /// <summary>Replace the custom files too.</summary>
    Replace,

    /// <summary>Leave the custom files alone and update the rest.</summary>
    Skip,

    /// <summary>Do nothing.</summary>
    Cancel,
}

/// <summary>
/// The dialogs and shell calls the view models need, supplied by each UI.
/// </summary>
public interface IUiServices
{
    Task<ReplaceCustomAnswer> AskReplaceCustomAsync(string title, string message);

    /// <summary>The chosen folder, or null when the user cancelled.</summary>
    Task<string?> PickFolderAsync(string title, string? initialFolder);

    Task ShowMessageAsync(string title, string message);

    /// <summary>Open a folder in the file manager or a URL in the browser.</summary>
    void Open(string target);
}
