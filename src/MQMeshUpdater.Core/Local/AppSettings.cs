using System.Text.Json;
using MQMeshUpdater.Core.Api;

namespace MQMeshUpdater.Core.Local;

public enum ZoneMode
{
    /// <summary>Follow the zone's Site file.</summary>
    Site,

    /// <summary>Keep one specific version.</summary>
    Version,

    /// <summary>Leave the local files alone.</summary>
    Unmanaged,
}

/// <summary>
/// What the user picked for one zone.
/// </summary>
public sealed class ZoneChoice
{
    public ZoneMode Mode { get; set; } = ZoneMode.Site;

    /// <summary>The chosen version when <see cref="Mode"/> is <see cref="ZoneMode.Version"/>.</summary>
    public FileSummary? Pinned { get; set; }

    public bool Doors { get; set; } = true;

    public bool IsDefault => Mode == ZoneMode.Site && Doors;

    public static ZoneChoice Default => new();

    public ZoneChoice Clone() => new() { Mode = Mode, Pinned = Pinned, Doors = Doors };
}

public sealed class WindowPlacement
{
    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool Maximized { get; set; }
}

public sealed class AppSettings
{
    /// <summary>The MacroQuest folder; null means the folder the exe is in.</summary>
    public string? MqFolder { get; set; }

    /// <summary>An explicit mesh folder; null means work it out from the MacroQuest folder.</summary>
    public string? MeshFolder { get; set; }

    public bool BackupBeforeOverwrite { get; set; } = true;

    public int ParallelDownloads { get; set; } = 4;

    /// <summary>Only the zones that differ from the default, keyed by short name.</summary>
    public Dictionary<string, ZoneChoice> Zones { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public WindowPlacement? Window { get; set; }

    public ZoneChoice ChoiceFor(string shortName) =>
        Zones.TryGetValue(shortName, out var choice) ? choice : ZoneChoice.Default;

    public void SetChoice(string shortName, ZoneChoice choice)
    {
        if (choice.IsDefault)
        {
            Zones.Remove(shortName);
        }
        else
        {
            Zones[shortName] = choice;
        }
    }

    public static AppSettings Load(AppPaths paths)
    {
        var json = AtomicFile.TryReadAllText(paths.Settings);

        if (json is null)
        {
            return new AppSettings();
        }

        try
        {
            var settings = Json.Deserialize<AppSettings>(json);

            // Keep lookups case-insensitive whatever the deserializer built.
            settings.Zones = new Dictionary<string, ZoneChoice>(settings.Zones, StringComparer.OrdinalIgnoreCase);
            settings.ParallelDownloads = Math.Clamp(settings.ParallelDownloads, 1, 8);

            return settings;
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppPaths paths) => AtomicFile.WriteAllText(paths.Settings, Json.Serialize(this));
}
