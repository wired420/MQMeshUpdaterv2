using System.Net;
using System.Net.Http.Headers;

namespace MQMeshUpdater.Core.Api;

/// <summary>
/// The result of a manifest request: either new JSON or "not modified".
/// </summary>
public sealed record ManifestResponse(bool NotModified, string? ETag, string? Json, Manifest? Manifest);

/// <summary>
/// Client for the mqmesh.com v1 API.
/// </summary>
public sealed class MeshApiClient
{
    public static readonly Uri DefaultBaseUri = new("https://mqmesh.com/api/v1/");

    private const int MaxAttempts = 4;
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(90);

    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MeshApiClient(HttpClient http, Uri? baseUri = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        BaseUri = baseUri ?? DefaultBaseUri;
        _delay = delay ?? Task.Delay;
    }

    public Uri BaseUri { get; }

    /// <summary>The last X-RateLimit-Remaining the API sent, if any.</summary>
    public int? RateLimitRemaining { get; private set; }

    /// <summary>
    /// Get the manifest with community versions. Pass the ETag from the last
    /// response to get <see cref="ManifestResponse.NotModified"/> when nothing changed.
    /// </summary>
    public async Task<ManifestResponse> GetManifestAsync(string? etag, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri, "manifest?include=community"));

            if (!string.IsNullOrEmpty(etag))
            {
                // Sent back exactly as received; the server compares the raw string.
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            return request;
        }, cancellationToken).ConfigureAwait(false);

        var newEtag = response.Headers.TryGetValues("ETag", out var values) ? values.FirstOrDefault() : null;

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new ManifestResponse(true, newEtag ?? etag, null, null);
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return new ManifestResponse(false, newEtag, json, Json.Deserialize<Manifest>(json));
    }

    /// <summary>
    /// The expansions in release order, as raw JSON for caching.
    /// </summary>
    public async Task<string> GetExpansionsJsonAsync(CancellationToken cancellationToken = default)
    {
        return await GetStringAsync("expansions", cancellationToken).ConfigureAwait(false);
    }

    public static List<Expansion> ParseExpansions(string json) => Json.Deserialize<DataEnvelope<Expansion>>(json).Data;

    /// <summary>
    /// One zone with its approved community versions.
    /// </summary>
    public async Task<ZoneSummary> GetZoneAsync(string shortName, CancellationToken cancellationToken = default)
    {
        var json = await GetStringAsync("zones/" + Uri.EscapeDataString(shortName), cancellationToken).ConfigureAwait(false);
        var zone = Json.Deserialize<ZoneSummary>(json);
        zone.Community ??= [];

        return zone;
    }

    /// <summary>
    /// The short names of every zone that has at least one community version.
    /// </summary>
    public async Task<List<string>> GetZonesWithCommunityAsync(CancellationToken cancellationToken = default)
    {
        var names = new List<string>();
        var page = 1;

        while (true)
        {
            var json = await GetStringAsync($"zones?community=1&per_page=500&page={page}", cancellationToken).ConfigureAwait(false);
            var result = Json.Deserialize<ZonesPage>(json);
            names.AddRange(result.Data.Select(z => z.ShortName));

            if (result.Meta.Page >= result.Meta.LastPage || result.Data.Count == 0)
            {
                return names;
            }

            page++;
        }
    }

    private async Task<string> GetStringAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri, path)), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Send one API request at a time, waiting out 429 responses.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            for (var attempt = 1; ; attempt++)
            {
                using var request = createRequest();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                RateLimitRemaining = ReadRemaining(response) ?? RateLimitRemaining;

                if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= MaxAttempts)
                {
                    // Close to the limit: slow down instead of running into it.
                    if (RateLimitRemaining is <= 2)
                    {
                        await _delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                    }

                    return response;
                }

                var wait = RetryAfter(response);
                response.Dispose();
                await _delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static TimeSpan RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        var wait = header?.Delta
            ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : DefaultRetryAfter);

        if (wait < TimeSpan.FromSeconds(1))
        {
            wait = TimeSpan.FromSeconds(1);
        }

        return wait > MaxRetryAfter ? MaxRetryAfter : wait;
    }

    private static int? ReadRemaining(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)
            && int.TryParse(values.FirstOrDefault(), out var remaining) ? remaining : null;
    }
}
