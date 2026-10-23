namespace MQMeshUpdater.Core.Local;

/// <summary>
/// Where the updater keeps its settings and caches.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? dataFolder = null)
    {
        DataFolder = dataFolder
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MQMeshUpdater");
    }

    public string DataFolder { get; }

    public string Settings => Path.Combine(DataFolder, "settings.json");

    public string Manifest => Path.Combine(DataFolder, "manifest.json");

    public string ManifestETag => Path.Combine(DataFolder, "manifest.etag");

    public string Expansions => Path.Combine(DataFolder, "expansions.json");

    public string HashCache => Path.Combine(DataFolder, "hashcache.json");

    public string Installed => Path.Combine(DataFolder, "installed.json");

    public void EnsureExists() => Directory.CreateDirectory(DataFolder);

    /// <summary>
    /// The folder MQ2Nav reads meshes from: an explicit choice, else
    /// resources\MQ2Nav or MQ2Nav, whichever already exists in the MacroQuest folder.
    /// </summary>
    public static string ResolveMeshFolder(string mqFolder, string? meshFolder)
    {
        if (!string.IsNullOrWhiteSpace(meshFolder))
        {
            return meshFolder;
        }

        var resources = Path.Combine(mqFolder, "resources", "MQ2Nav");
        var legacy = Path.Combine(mqFolder, "MQ2Nav");

        if (Directory.Exists(resources))
        {
            return resources;
        }

        if (Directory.Exists(legacy))
        {
            return legacy;
        }

        return Directory.Exists(Path.Combine(mqFolder, "resources")) ? resources : legacy;
    }
}

/// <summary>
/// Small JSON files written so a crash never leaves half a file behind.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }

    public static string? TryReadAllText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
