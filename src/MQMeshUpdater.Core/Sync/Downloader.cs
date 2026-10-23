using System.Security.Cryptography;
using MQMeshUpdater.Core.Api;
using MQMeshUpdater.Core.Local;

namespace MQMeshUpdater.Core.Sync;

public sealed record DownloadJob(string ZoneShortName, FileSummary File, string TargetPath);

public sealed record DownloadResult(DownloadJob Job, bool Success, string? Error);

public sealed record DownloadReport(IReadOnlyList<DownloadResult> Results, bool Cancelled)
{
    public int Succeeded => Results.Count(r => r.Success);

    public int Failed => Results.Count(r => !r.Success);
}

public enum JobState
{
    Started,
    Progress,
    Done,
    Failed,
}

public sealed record DownloadProgress(
    DownloadJob Job,
    JobState State,
    long JobBytes,
    long OverallBytes,
    long OverallTotal,
    int Completed,
    int Failed,
    int Total,
    string? Error = null);

public sealed class DownloadOptions
{
    public int Parallel { get; init; } = 4;

    /// <summary>Copy files here before replacing them; null to skip backups.</summary>
    public string? BackupFolder { get; init; }

    public int Retries { get; init; } = 2;
}

/// <summary>
/// Downloads files to a temporary .part file, checks the size and SHA-256,
/// then swaps it into place.
/// </summary>
public sealed class Downloader
{
    private const int ProgressStep = 256 * 1024;

    private readonly HttpClient _http;

    public Downloader(HttpClient http)
    {
        _http = http;
    }

    public async Task<DownloadReport> RunAsync(
        IReadOnlyList<DownloadJob> jobs,
        DownloadOptions options,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new DownloadResult[jobs.Count];
        var overallTotal = jobs.Sum(j => j.File.Size);
        long overallBytes = 0;
        int completed = 0, failed = 0;

        void Report(DownloadJob job, JobState state, long jobBytes, string? error = null) =>
            progress?.Report(new DownloadProgress(job, state, jobBytes, Interlocked.Read(ref overallBytes), overallTotal,
                Volatile.Read(ref completed), Volatile.Read(ref failed), jobs.Count, error));

        var cancelled = false;

        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, jobs.Count),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Parallel), CancellationToken = cancellationToken },
                async (index, token) =>
                {
                    var job = jobs[index];
                    string? error = null;
                    Report(job, JobState.Started, 0);

                    for (var attempt = 0; attempt <= options.Retries; attempt++)
                    {
                        long counted = 0;

                        try
                        {
                            await DownloadOneAsync(job, options, bytes =>
                            {
                                Interlocked.Add(ref overallBytes, bytes - counted);
                                counted = bytes;
                                Report(job, JobState.Progress, bytes);
                            }, token).ConfigureAwait(false);

                            error = null;
                            break;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Interlocked.Add(ref overallBytes, -counted);
                            error = ex.Message;
                        }
                    }

                    if (error is null)
                    {
                        Interlocked.Increment(ref completed);
                        results[index] = new DownloadResult(job, true, null);
                        Report(job, JobState.Done, job.File.Size);
                    }
                    else
                    {
                        Interlocked.Increment(ref failed);
                        results[index] = new DownloadResult(job, false, error);
                        Report(job, JobState.Failed, 0, error);
                    }
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        return new DownloadReport(results.Where(r => r is not null).ToList(), cancelled);
    }

    private async Task DownloadOneAsync(DownloadJob job, DownloadOptions options, Action<long> onBytes, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(job.TargetPath)!;
        Directory.CreateDirectory(folder);

        var part = job.TargetPath + ".part";

        try
        {
            using (var response = await _http.GetAsync(FileUrls.ForDownload(job.File), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

                var buffer = new byte[1 << 16];
                long total = 0, reported = 0;
                int read;

                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    sha256.AppendData(buffer, 0, read);
                    total += read;

                    if (total - reported >= ProgressStep)
                    {
                        reported = total;
                        onBytes(total);
                    }
                }

                onBytes(total);

                if (total != job.File.Size)
                {
                    throw new InvalidDataException($"{job.File.FileName}: got {total:N0} bytes, expected {job.File.Size:N0}.");
                }

                var hash = Hashing.ToHex(sha256.GetHashAndReset());

                if (!Hashing.Same(hash, job.File.Sha256))
                {
                    throw new InvalidDataException($"{job.File.FileName}: SHA-256 does not match, the download was damaged.");
                }
            }

            if (options.BackupFolder is not null && File.Exists(job.TargetPath))
            {
                Directory.CreateDirectory(options.BackupFolder);
                File.Copy(job.TargetPath, Path.Combine(options.BackupFolder, Path.GetFileName(job.TargetPath)), overwrite: true);
            }

            File.Move(part, job.TargetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(part))
            {
                File.Delete(part);
            }
        }
    }
}
