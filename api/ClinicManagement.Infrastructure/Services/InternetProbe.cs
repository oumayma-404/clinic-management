using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Judges the server's internet egress by issuing a short-timeout request to a configurable probe URL
/// (any 2xx/3xx ⇒ reachable). Registered as a <b>Singleton</b> so the <see cref="IMemoryCache"/> result
/// is genuinely shared across requests; a <see cref="SemaphoreSlim"/> collapses a burst of concurrent
/// polls into a single outbound probe per cache-TTL window (R-1/R-2). <see cref="IHttpClientFactory"/>
/// is safe to inject into a singleton.
/// </summary>
public class InternetProbe : IInternetProbe
{
    private const string CacheKey = "connectivity:internet-reachable";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InternetProbe> _logger;
    private readonly SemaphoreSlim _probeLock = new(1, 1);

    public InternetProbe(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<InternetProbe> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> IsInternetReachableAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out bool cached))
        {
            return cached;
        }

        await _probeLock.WaitAsync(cancellationToken);
        try
        {
            // A concurrent caller may have populated the cache while we waited on the lock —
            // re-check so only one outbound probe fires per TTL window.
            if (_cache.TryGetValue(CacheKey, out bool cachedAfterWait))
            {
                return cachedAfterWait;
            }

            var reachable = await ProbeAsync(cancellationToken);
            var ttl = TimeSpan.FromSeconds(ConnectivityConfig.ProbeCacheSeconds(_configuration));
            _cache.Set(CacheKey, reachable, ttl);
            return reachable;
        }
        finally
        {
            _probeLock.Release();
        }
    }

    private async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        var url = ConnectivityConfig.ProbeUrl(_configuration);
        var timeout = TimeSpan.FromSeconds(ConnectivityConfig.ProbeTimeoutSeconds(_configuration));

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

            // Any 2xx/3xx means the server reached the internet host. (A captive portal returning 200
            // is a known false-positive; the probe URL is configurable to work around it — R-2.)
            var status = (int)response.StatusCode;
            return status is >= 200 and < 400;
        }
        catch (Exception ex)
        {
            // Timeout, DNS failure, connection refused, etc. ⇒ treat internet as unreachable.
            _logger.LogDebug(ex, "Internet probe to {Url} failed; treating internet as unreachable.", url);
            return false;
        }
    }
}
