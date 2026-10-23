using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using MQMeshUpdater.Core.Api;

namespace MQMeshUpdater.Core.Local;

/// <summary>
/// A local file's size and hashes, as lowercase hex.
/// </summary>
public sealed record LocalFile(string Path, long Size, string Md5, string Sha256);

public static class Hashing
{
    /// <summary>
    /// MD5 and SHA-256 of a file in one pass, as lowercase hex.
    /// </summary>
    public static async Task<(string Md5, string Sha256)> HashFileAsync(string path, CancellationToken cancellationToken = default)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);

        var buffer = new byte[1 << 16];
        int read;

        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            md5.AppendData(buffer, 0, read);
            sha256.AppendData(buffer, 0, read);
        }

        return (ToHex(md5.GetHashAndReset()), ToHex(sha256.GetHashAndReset()));
    }

    public static string ToHex(byte[] hash) => Convert.ToHexStringLower(hash);

    public static bool Same(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Remembers local files' hashes by path, size and modified time, so a
/// restart only hashes files that changed.
/// </summary>
public sealed class HashCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries;

    public HashCache(IDictionary<string, Entry>? entries = null)
    {
        _entries = new ConcurrentDictionary<string, Entry>(entries ?? new Dictionary<string, Entry>(), StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _entries.Count;

    /// <summary>
    /// The file's hashes, or null when it does not exist.
    /// </summary>
    public async Task<LocalFile?> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);

        if (!info.Exists)
        {
            _entries.TryRemove(path, out _);
            return null;
        }

        var ticks = info.LastWriteTimeUtc.Ticks;

        if (_entries.TryGetValue(path, out var cached) && cached.Size == info.Length && cached.Modified == ticks)
        {
            return new LocalFile(path, cached.Size, cached.Md5, cached.Sha256);
        }

        var (md5, sha256) = await Hashing.HashFileAsync(path, cancellationToken).ConfigureAwait(false);
        _entries[path] = new Entry(info.Length, ticks, md5, sha256);

        return new LocalFile(path, info.Length, md5, sha256);
    }

    public void Forget(string path) => _entries.TryRemove(path, out _);

    public static HashCache Load(AppPaths paths)
    {
        var json = AtomicFile.TryReadAllText(paths.HashCache);

        try
        {
            return new HashCache(json is null ? null : Json.Deserialize<Dictionary<string, Entry>>(json));
        }
        catch (JsonException)
        {
            return new HashCache();
        }
    }

    public void Save(AppPaths paths) =>
        AtomicFile.WriteAllText(paths.HashCache, Json.Serialize(new SortedDictionary<string, Entry>(_entries, StringComparer.OrdinalIgnoreCase)));

    public sealed record Entry(long Size, long Modified, string Md5, string Sha256);
}

/// <summary>
/// The SHA-256 of every file this updater wrote, by path. A local file that
/// still matches is one of ours and safe to replace; anything else that
/// matches no known version is the user's own.
/// </summary>
public sealed class InstalledLedger
{
    private readonly ConcurrentDictionary<string, string> _files;

    public InstalledLedger(IDictionary<string, string>? files = null)
    {
        _files = new ConcurrentDictionary<string, string>(files ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
    }

    public string? Get(string path) => _files.TryGetValue(path, out var sha256) ? sha256 : null;

    public void Record(string path, string sha256) => _files[path] = sha256.ToLowerInvariant();

    public static InstalledLedger Load(AppPaths paths)
    {
        var json = AtomicFile.TryReadAllText(paths.Installed);

        try
        {
            return new InstalledLedger(json is null ? null : Json.Deserialize<Dictionary<string, string>>(json));
        }
        catch (JsonException)
        {
            return new InstalledLedger();
        }
    }

    public void Save(AppPaths paths) =>
        AtomicFile.WriteAllText(paths.Installed, Json.Serialize(new SortedDictionary<string, string>(_files, StringComparer.OrdinalIgnoreCase)));
}
