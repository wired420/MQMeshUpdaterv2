using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MQMeshUpdater.Core.Sync;

namespace MQMeshUpdater.Linux;

/// <summary>
/// The colour for a zone's status dot and label.
/// </summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public static readonly StatusBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ZoneStatus.UpToDate => "OkBrush",
            ZoneStatus.UpdateAvailable => "UpdateBrush",
            ZoneStatus.Missing => "MissingBrush",
            ZoneStatus.Custom => "CustomBrush",
            _ => "MutedBrush",
        };

        return Application.Current?.TryGetResource(key, null, out var brush) == true ? brush : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
