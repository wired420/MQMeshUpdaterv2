namespace MQMeshUpdater.Core.Local;

/// <summary>
/// Where the updater keeps its settings and caches.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? dataFolder = null)
    {
        DataFolder = dataFolder
            ?? NonEmpty(Environment.GetEnvironmentVariable("MQMESHUPDATER_DATA"))
            ?? Path.Combine(DefaultRoot(), "MQMeshUpdater");
    }

    public string DataFolder { get; }

    public string Settings => Path.Combine(DataFolder, "settings.json");

    public string Manifest => Path.Combine(DataFolder, "manifest.json");

    public string ManifestETag => Path.Combine(DataFolder, "manifest.etag");

    public string Expansions => Path.Combine(DataFolder, "expansions.json");

    public string HashCache => Path.Combine(DataFolder, "hashcache.json");

    public string Installed => Path.Combine(DataFolder, "installed.json");

    public void EnsureExists() => Directory.CreateDirectory(DataFolder);

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// %LOCALAPPDATA% on Windows, $XDG_DATA_HOME or ~/.local/share on Linux,
    /// or the exe's folder when neither is known (no HOME).
    /// </summary>
    private static string DefaultRoot() =>
        NonEmpty(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) ?? AppContext.BaseDirectory;

    /// <summary>
    /// The folder MQ2Nav reads meshes from: an explicit choice, else
    /// resources/MQ2Nav or MQ2Nav, whichever already exists in the MacroQuest folder.
    /// Names are matched ignoring case, so a Wine prefix's "mq2nav" is found on Linux too.
    /// </summary>
    public static string ResolveMeshFolder(string mqFolder, string? meshFolder)
    {
        if (!string.IsNullOrWhiteSpace(meshFolder))
        {
            return meshFolder;
        }

        var resources = CaseInsensitive.FindDirectory(mqFolder, "resources");

        if (resources is not null && CaseInsensitive.FindDirectory(resources, "MQ2Nav") is { } nav)
        {
            return nav;
        }

        if (CaseInsensitive.FindDirectory(mqFolder, "MQ2Nav") is { } legacy)
        {
            return legacy;
        }

        return resources is not null ? Path.Combine(resources, "MQ2Nav") : Path.Combine(mqFolder, "MQ2Nav");
    }
}

/// <summary>
/// Finds files and folders the way Windows (and Wine) would, ignoring case,
/// on file systems that do not.
/// </summary>
public static class CaseInsensitive
{
    /// <summary>
    /// The child folder with this name, spelled as it is on disk, or null.
    /// An exact match wins over one that differs only by case.
    /// </summary>
    public static string? FindDirectory(string parent, string name)
    {
        try
        {
            var matches = Directory.EnumerateDirectories(parent)
                .Where(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return matches.FirstOrDefault(d => Path.GetFileName(d) == name) ?? matches.FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The files in a folder by name, ignoring case. When names differ only by
    /// case, the all-lowercase one (as mqmesh.com serves them) wins.
    /// </summary>
    public static Dictionary<string, string> FilesByName(string folder)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(path);

                if (!files.ContainsKey(name) || name == name.ToLowerInvariant())
                {
                    files[name] = path;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return files;
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
