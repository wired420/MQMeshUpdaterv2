namespace MQMeshUpdater.Core.Api;

public static class FileUrls
{
    private const string ApiSegment = "/api/v1/files/";

    /// <summary>
    /// The URL to fetch a file's contents from.
    /// </summary>
    public static Uri ForDownload(FileSummary file)
    {
        var url = file.DownloadUrl;
        var index = url.IndexOf(ApiSegment, StringComparison.OrdinalIgnoreCase);

        if (index >= 0)
        {
            url = string.Concat(url.AsSpan(0, index), "/files/", url.AsSpan(index + ApiSegment.Length));
        }

        return new Uri(url);
    }
}
