using CommunityToolkit.Mvvm.ComponentModel;

namespace MQMeshUpdater.ViewModels;

/// <summary>
/// An expansion heading in the zone list.
/// </summary>
public sealed partial class ExpansionGroupViewModel : ObservableObject
{
    private bool _setting;

    public ExpansionGroupViewModel(string code, string name, int order)
    {
        Code = code;
        Name = name;
        Order = order;
    }

    public string Code { get; }

    public string Name { get; }

    public int Order { get; }

    public List<ZoneRowViewModel> Rows { get; } = [];

    /// <summary>Whether the group's zones are shown, for lists that draw their own headings.</summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    public event EventHandler? ExpandedChanged;

    partial void OnIsExpandedChanged(bool value) => ExpandedChanged?.Invoke(this, EventArgs.Empty);

    public string Summary
    {
        get
        {
            var attention = Rows.Count(r => r.NeedsAttention);
            var text = $"{Rows.Count} zones";

            return attention == 0 ? text : $"{text} · {attention} to update";
        }
    }

    /// <summary>Ticked when every zone is managed, unticked when none are, otherwise mixed.</summary>
    public bool? AllManaged
    {
        get
        {
            var managed = Rows.Count(r => r.IsManaged);

            return managed == Rows.Count ? true : managed == 0 ? false : null;
        }
        set
        {
            // A click on a mixed box arrives as null: treat it as "manage all".
            var manage = value ?? true;
            _setting = true;

            try
            {
                foreach (var row in Rows)
                {
                    row.IsManaged = manage;
                }
            }
            finally
            {
                _setting = false;
            }

            Refresh();
        }
    }

    public void Refresh()
    {
        if (_setting)
        {
            return;
        }

        OnPropertyChanged(nameof(AllManaged));
        OnPropertyChanged(nameof(Summary));
    }
}
