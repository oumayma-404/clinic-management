using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>How a call to the cloud ended, as the PC de secours has to act on it.</summary>
public enum RelayCallStatus
{
    Ok,
    /// <summary>No answer — the internet or the cloud is down. Nothing is concluded from it.</summary>
    Unreachable,
    /// <summary>The cloud retired this PC, or no longer knows it (AC-8.1).</summary>
    Released,
    /// <summary>The cloud runs another build; copy calls wait for this PC's update (D10b).</summary>
    UpdateNeeded,
    NotFound,
    /// <summary>Any other refusal, with the cloud's own sentence.</summary>
    Refused,
}

public sealed record RelayCall<T>(RelayCallStatus Status, T? Value = default, string? Error = null)
{
    public bool IsOk => Status == RelayCallStatus.Ok;
}

/// <summary>What the PC de secours asks of its cloud (the <c>RelayPeerController</c> routes).</summary>
public interface IRelayCloudClient
{
    Task<RelayCall<RelayHeartbeatAck>> HeartbeatAsync(RelayHeartbeatRequest report, CancellationToken cancellationToken);

    Task<RelayCall<RelayFeedBatch>> ChangesAsync(long after, string? fingerprint, CancellationToken cancellationToken);

    /// <summary>Downloads a snapshot (gzip'd JSON) to a temporary file the caller deletes.</summary>
    Task<RelayCall<string>> SnapshotAsync(IReadOnlyCollection<string>? tables, CancellationToken cancellationToken);

    Task<RelayCall<IReadOnlyList<RelayTableDigest>>> DigestAsync(CancellationToken cancellationToken);

    /// <summary>Downloads one stored object to a temporary file the caller deletes.</summary>
    Task<RelayCall<string>> BlobAsync(string storageKey, CancellationToken cancellationToken);
}

/// <summary>
/// The PC's HTTP side. Holds the short <c>clinic-relay</c> token between calls and trades the long-lived secret for a new
/// one when it expires; the secret travels in a header, never in a URL (URLs are logged).
/// </summary>
public sealed class RelayCloudClient : IRelayCloudClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly RelayCredentials _credentials;
    private readonly string _build;
    private readonly Func<DateTime> _utcNow;
    private string? _token;
    private DateTime _tokenExpiresAtUtc;

    public RelayCloudClient(HttpClient http, RelayCredentials credentials, string build, Func<DateTime>? utcNow = null)
    {
        _http = http;
        _http.BaseAddress ??= credentials.ApiBase;
        _credentials = credentials;
        _build = build;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public Task<RelayCall<RelayHeartbeatAck>> HeartbeatAsync(RelayHeartbeatRequest report, CancellationToken cancellationToken) =>
        SendJsonAsync<RelayHeartbeatAck>(() => new HttpRequestMessage(HttpMethod.Post, "relay/heartbeat")
        {
            Content = JsonContent.Create(report, options: Json),
        }, cancellationToken);

    public Task<RelayCall<RelayFeedBatch>> ChangesAsync(long after, string? fingerprint, CancellationToken cancellationToken) =>
        SendJsonAsync<RelayFeedBatch>(() => new HttpRequestMessage(HttpMethod.Get,
            $"relay/changes?after={after}&fingerprint={Uri.EscapeDataString(fingerprint ?? string.Empty)}"), cancellationToken);

    public Task<RelayCall<string>> SnapshotAsync(IReadOnlyCollection<string>? tables, CancellationToken cancellationToken) =>
        DownloadAsync(() => new HttpRequestMessage(HttpMethod.Get,
            tables is { Count: > 0 } ? $"relay/snapshot?tables={Uri.EscapeDataString(string.Join(',', tables))}" : "relay/snapshot"),
            ".json.gz", cancellationToken);

    public Task<RelayCall<IReadOnlyList<RelayTableDigest>>> DigestAsync(CancellationToken cancellationToken) =>
        SendJsonAsync<IReadOnlyList<RelayTableDigest>>(() => new HttpRequestMessage(HttpMethod.Get, "relay/digest"), cancellationToken);

    public Task<RelayCall<string>> BlobAsync(string storageKey, CancellationToken cancellationToken) =>
        DownloadAsync(() => new HttpRequestMessage(HttpMethod.Get, $"relay/blob?key={Uri.EscapeDataString(storageKey)}"),
            ".part", cancellationToken);

    private async Task<RelayCall<T>> SendJsonAsync<T>(Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        var (status, response, error) = await SendAsync(build, cancellationToken);
        using (response)
        {
            if (status != RelayCallStatus.Ok)
            {
                return new RelayCall<T>(status, default, error);
            }

            var value = await response!.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
            return value is null
                ? new RelayCall<T>(RelayCallStatus.Refused, default, "Réponse vide du cloud.")
                : new RelayCall<T>(RelayCallStatus.Ok, value);
        }
    }

    private async Task<RelayCall<string>> DownloadAsync(
        Func<HttpRequestMessage> build, string extension, CancellationToken cancellationToken)
    {
        var (status, response, error) = await SendAsync(build, cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        using (response)
        {
            if (status != RelayCallStatus.Ok)
            {
                return new RelayCall<string>(status, null, error);
            }

            var path = Path.Combine(Path.GetTempPath(), $"relay-{Guid.NewGuid():N}{extension}");
            try
            {
                await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await response!.Content.CopyToAsync(target, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                TryDelete(path);
                return new RelayCall<string>(RelayCallStatus.Unreachable, null, ex.Message);
            }

            return new RelayCall<string>(RelayCallStatus.Ok, path);
        }
    }

    /// <summary>Sends with the current token, refreshing it once on a 401 that is not « unknown PC ».</summary>
    private async Task<(RelayCallStatus Status, HttpResponseMessage? Response, string? Error)> SendAsync(
        Func<HttpRequestMessage> build, CancellationToken cancellationToken,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await TokenAsync(forceRefresh: attempt > 0, cancellationToken);
            if (token.Status != RelayCallStatus.Ok)
            {
                return (token.Status, null, token.Error);
            }

            using var request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
            request.Headers.Add("X-Relay-Build", _build);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, completion, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return (RelayCallStatus.Unreachable, null, ex.Message);
            }

            if (response.IsSuccessStatusCode)
            {
                return (RelayCallStatus.Ok, response, null);
            }

            var (code, error) = await RefusalAsync(response, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && code is null && attempt == 0)
            {
                response.Dispose();
                continue;
            }

            response.Dispose();
            return (Classify(response.StatusCode, code), null, error);
        }

        return (RelayCallStatus.Released, null, RelayRefusals.UnknownRelay);
    }

    private async Task<RelayCall<string>> TokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _token is not null && _utcNow() < _tokenExpiresAtUtc - TimeSpan.FromMinutes(1))
        {
            return new RelayCall<string>(RelayCallStatus.Ok, _token);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "relay/token")
        {
            Content = JsonContent.Create(new { relayId = _credentials.RelayId }, options: Json),
        };
        request.Headers.Add("X-Relay-Secret", _credentials.Secret);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new RelayCall<string>(RelayCallStatus.Unreachable, null, ex.Message);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var (code, error) = await RefusalAsync(response, cancellationToken);
                return new RelayCall<string>(Classify(response.StatusCode, code), null, error);
            }

            var token = await response.Content.ReadFromJsonAsync<RelayTokenDto>(Json, cancellationToken);
            if (token is null || string.IsNullOrEmpty(token.AccessToken))
            {
                return new RelayCall<string>(RelayCallStatus.Refused, null, "Jeton vide du cloud.");
            }

            _token = token.AccessToken;
            _tokenExpiresAtUtc = token.ExpiresAt;
            return new RelayCall<string>(RelayCallStatus.Ok, _token);
        }
    }

    /// <summary>The PC acts on the refusal's code, never on its sentence.</summary>
    public static RelayCallStatus Classify(HttpStatusCode status, string? code) => code switch
    {
        RelayRefusals.RetiredCode or RelayRefusals.UnknownRelayCode => RelayCallStatus.Released,
        RelayRefusals.VersionMismatchCode => RelayCallStatus.UpdateNeeded,
        _ => status switch
        {
            HttpStatusCode.NotFound => RelayCallStatus.NotFound,
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.TooManyRequests => RelayCallStatus.Unreachable,
            _ => RelayCallStatus.Refused,
        },
    };

    private static async Task<(string? Code, string? Error)> RefusalAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var code = body.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
            var error = body.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (code, error ?? $"HTTP {(int)response.StatusCode}");
        }
        catch (JsonException)
        {
            return (null, $"HTTP {(int)response.StatusCode}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
