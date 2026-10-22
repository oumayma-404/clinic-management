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
    /// <summary>The cloud retired this PC (AC-8.1).</summary>
    Released,
    /// <summary>The cloud runs another build; copy calls wait for this PC's update (D10b).</summary>
    UpdateNeeded,
    NotFound,
    /// <summary>Any other refusal, with the cloud's own sentence.</summary>
    Refused,
    /// <summary>
    /// The cloud answered and knows no such PC. ⚠️ Not <see cref="Released"/> (review finding 9): a cloud restored from
    /// before the pairing forgets it, and this copy then holds what the cloud lost — it is kept, never released.
    /// </summary>
    Unknown,
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

    /// <summary>« Effacer la copie » is done (AC-8.2) — sent with the PC's secret, since a retired PC gets no token.</summary>
    Task<RelayCall<bool>> ReportErasedAsync(CancellationToken cancellationToken);

    /// <summary>The PC is being uninstalled (AC-8.3) — the PC's secret, like the erase report.</summary>
    Task<RelayCall<bool>> ReportUninstalledAsync(CancellationToken cancellationToken);

    /// <summary>D18: which of the files the cut's rows name the cloud does not hold yet.</summary>
    Task<RelayCall<IReadOnlyList<string>>> MissingHandbackFilesAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken);

    /// <summary>D18: one file made during the cut, under the key its row names.</summary>
    Task<RelayCall<bool>> UploadHandbackFileAsync(string storageKey, Stream content, CancellationToken cancellationToken);

    /// <summary>D18 phase 1: the cut's work, handed back once.</summary>
    Task<RelayCall<RelayHandbackResultDto>> HandBackAsync(RelayHandbackRequest request, CancellationToken cancellationToken);

    /// <summary>US-7: an overruled cut's work, listed « À reprendre » on the cloud.</summary>
    Task<RelayCall<RelayHandbackResultDto>> ListOverruledCutAsync(RelayHandbackRequest request, CancellationToken cancellationToken);

    /// <summary>AC-9.4: a restored cloud's row hashes for one table.</summary>
    Task<RelayCall<IReadOnlyList<RelayRowHashDto>>> GapHashesAsync(string table, CancellationToken cancellationToken);

    /// <summary>AC-9.4: what the restore lost, sent back once (same id = no-op).</summary>
    Task<RelayCall<RelayHandbackResultDto>> ReturnGapAsync(RelayGapRequest request, CancellationToken cancellationToken);
}

/// <summary>D16: the PC's long poll for the numbers its cloud is about to make final.</summary>
public interface IRelayPromiseChannel
{
    /// <summary>Acknowledges the promises kept, then waits (about 20 s) for the next ones.</summary>
    Task<RelayCall<IReadOnlyList<RelayNumberPromiseDto>>> PromisesAsync(
        IReadOnlyList<Guid> acks, CancellationToken cancellationToken);
}

/// <summary>
/// The PC's HTTP side. Holds the short <c>clinic-relay</c> token between calls and trades the long-lived secret for a new
/// one when it expires; the secret travels in a header, never in a URL (URLs are logged).
/// </summary>
public sealed class RelayCloudClient : IRelayCloudClient, IRelayPromiseChannel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// A transfer that has moved no byte for this long is a dead line, not a slow one (review finding 6): a download read
    /// with nothing but the service's own token could wait for ever across a cut — the copy tick with it, and the return
    /// that only that tick drives.
    /// </summary>
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>After the last byte of a long upload, how long the cloud may take to store it and answer.</summary>
    public static readonly TimeSpan AnswerAfterUpload = TimeSpan.FromMinutes(5);

    /// <summary>The ceiling of a return's POST: the cloud applies a whole cut before it answers (review finding 7).</summary>
    public static readonly TimeSpan HandbackAnswerWithin = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly HttpClient _transfers;
    private readonly RelayCredentials _credentials;
    private readonly string _build;
    private readonly Func<DateTime> _utcNow;
    private string? _token;

    // Files are fetched several at a time now: one refresh at once, the others reuse its token.
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private DateTime _tokenExpiresAtUtc;

    /// <param name="transfers">
    /// For what may take longer than <paramref name="http"/>'s 100 s ceiling — a file of the cut, the cut itself — with no
    /// overall timeout of its own: those calls are bounded by <see cref="StallTimeout"/> (bytes) or their own ceiling.
    /// Without it, a radiograph over ~10 MB on an ADSL uplink could never go back, and the return looped for ever.
    /// </param>
    public RelayCloudClient(
        HttpClient http, RelayCredentials credentials, string build, Func<DateTime>? utcNow = null, HttpClient? transfers = null)
    {
        _http = http;
        _http.BaseAddress ??= credentials.ApiBase;
        _transfers = transfers ?? http;
        _transfers.BaseAddress ??= credentials.ApiBase;
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

    public Task<RelayCall<IReadOnlyList<RelayNumberPromiseDto>>> PromisesAsync(
        IReadOnlyList<Guid> acks, CancellationToken cancellationToken) =>
        SendJsonAsync<IReadOnlyList<RelayNumberPromiseDto>>(() => new HttpRequestMessage(HttpMethod.Post, "relay/promises")
        {
            Content = JsonContent.Create(new RelayPromisesRequest(acks), options: Json),
        }, cancellationToken);

    public Task<RelayCall<IReadOnlyList<string>>> MissingHandbackFilesAsync(
        IReadOnlyList<string> keys, CancellationToken cancellationToken) =>
        SendJsonAsync<IReadOnlyList<string>>(() => new HttpRequestMessage(HttpMethod.Post, "relay/handback/files")
        {
            Content = JsonContent.Create(new RelayHandbackFilesRequest(keys), options: Json),
        }, cancellationToken);

    public async Task<RelayCall<bool>> UploadHandbackFileAsync(
        string storageKey, Stream content, CancellationToken cancellationToken)
    {
        // One attempt: the stream is read as it is sent, so it cannot be replayed after a refreshed token. No overall
        // ceiling — a panoramic on an ADSL uplink takes minutes — only a stall: no byte read for StallTimeout ends it.
        using var watch = new TransferWatch(cancellationToken, StallTimeout);
        try
        {
            var (status, response, error) = await SendAsync(() =>
            {
                var body = new StreamContent(new WatchedStream(content, watch));
                body.Headers.ContentLength = content.CanSeek ? content.Length - content.Position : null;
                return new HttpRequestMessage(HttpMethod.Put, $"relay/handback/file?key={Uri.EscapeDataString(storageKey)}")
                {
                    Content = body,
                };
            }, watch.Token, singleAttempt: true, via: _transfers);
            response?.Dispose();
            return new RelayCall<bool>(status, status == RelayCallStatus.Ok, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new RelayCall<bool>(RelayCallStatus.Unreachable, false, "Le cloud ne reçoit plus le fichier.");
        }
    }

    public Task<RelayCall<RelayHandbackResultDto>> HandBackAsync(
        RelayHandbackRequest request, CancellationToken cancellationToken) =>
        SendLongJsonAsync<RelayHandbackResultDto>(() => new HttpRequestMessage(HttpMethod.Post, "relay/handback")
        {
            Content = JsonContent.Create(request, options: Json),
        }, cancellationToken);

    public Task<RelayCall<RelayHandbackResultDto>> ListOverruledCutAsync(
        RelayHandbackRequest request, CancellationToken cancellationToken) =>
        SendLongJsonAsync<RelayHandbackResultDto>(() => new HttpRequestMessage(HttpMethod.Post, "relay/handback/overruled")
        {
            Content = JsonContent.Create(request, options: Json),
        }, cancellationToken);

    public Task<RelayCall<IReadOnlyList<RelayRowHashDto>>> GapHashesAsync(string table, CancellationToken cancellationToken) =>
        SendJsonAsync<IReadOnlyList<RelayRowHashDto>>(
            () => new HttpRequestMessage(HttpMethod.Get, "relay/gap/hashes?table=" + Uri.EscapeDataString(table)), cancellationToken);

    public Task<RelayCall<RelayHandbackResultDto>> ReturnGapAsync(RelayGapRequest request, CancellationToken cancellationToken) =>
        SendLongJsonAsync<RelayHandbackResultDto>(() => new HttpRequestMessage(HttpMethod.Post, "relay/gap")
        {
            Content = JsonContent.Create(request, options: Json),
        }, cancellationToken);

    public Task<RelayCall<bool>> ReportErasedAsync(CancellationToken cancellationToken) =>
        ReportWithSecretAsync("relay/erased", cancellationToken);

    public Task<RelayCall<bool>> ReportUninstalledAsync(CancellationToken cancellationToken) =>
        ReportWithSecretAsync("relay/uninstalled", cancellationToken);

    /// <summary>A retired PC gets no token, so these reports carry the secret in a header (never the URL).</summary>
    private async Task<RelayCall<bool>> ReportWithSecretAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { relayId = _credentials.RelayId }, options: Json),
        };
        request.Headers.Add("X-Relay-Secret", _credentials.Secret);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new RelayCall<bool>(RelayCallStatus.Ok, true);
            }

            var (code, error) = await RefusalAsync(response, cancellationToken);
            return new RelayCall<bool>(Classify(response.StatusCode, code), false, error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new RelayCall<bool>(RelayCallStatus.Unreachable, false, ex.Message);
        }
    }

    /// <summary>A call the cloud answers only once it has applied a whole cut: its own ceiling, not the 100 s one.</summary>
    private async Task<RelayCall<T>> SendLongJsonAsync<T>(Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        using var ceiling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ceiling.CancelAfter(HandbackAnswerWithin);
        try
        {
            return await SendJsonAsync<T>(build, ceiling.Token, _transfers);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new RelayCall<T>(RelayCallStatus.Unreachable, default, "Le cloud n'a pas répondu à temps.");
        }
    }

    private async Task<RelayCall<T>> SendJsonAsync<T>(
        Func<HttpRequestMessage> build, CancellationToken cancellationToken, HttpClient? via = null)
    {
        var (status, response, error) = await SendAsync(build, cancellationToken, via: via);
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
                await using var body = await response!.Content.ReadAsStreamAsync(cancellationToken);
                await CopyUntilStalledAsync(body, target, StallTimeout, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                TryDelete(path);
                return new RelayCall<string>(RelayCallStatus.Unreachable, null, ex.Message);
            }
            catch (OperationCanceledException)
            {
                TryDelete(path);
                throw;
            }

            return new RelayCall<string>(RelayCallStatus.Ok, path);
        }
    }

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="target"/>, giving up — as an <see cref="IOException"/>, i.e. « no
    /// answer » — once no byte has arrived for <paramref name="stall"/>. A read across a dead line otherwise waits for ever:
    /// HttpClient's timeout ends at the headers, and the PC sends nothing that could notice the line is gone.
    /// </summary>
    public static async Task CopyUntilStalledAsync(Stream source, Stream target, TimeSpan stall, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (true)
        {
            int read;
            using (var quiet = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                quiet.CancelAfter(stall);
                try
                {
                    read = await source.ReadAsync(buffer, quiet.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new IOException($"Le cloud n'envoie plus rien depuis {(int)stall.TotalSeconds} s.");
                }
            }

            if (read == 0)
            {
                return;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    /// <summary>
    /// Ends a transfer that stopped moving: each byte read pushes the deadline back by the stall time, and once the last one
    /// has gone the cloud gets <see cref="AnswerAfterUpload"/> to store it and answer.
    /// </summary>
    private sealed class TransferWatch : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly TimeSpan _stall;
        private readonly Timer _timer;
        private long _deadline;

        public TransferWatch(CancellationToken outer, TimeSpan stall)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            _stall = stall;
            Moved();
            _timer = new Timer(_ =>
            {
                if (Environment.TickCount64 <= Interlocked.Read(ref _deadline))
                {
                    return;
                }

                try
                {
                    _cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // A tick already running when the transfer ended: an exception here would end the process.
                }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public CancellationToken Token => _cts.Token;

        public void Moved() => Interlocked.Exchange(ref _deadline, Environment.TickCount64 + (long)_stall.TotalMilliseconds);

        public void Sent() =>
            Interlocked.Exchange(ref _deadline, Environment.TickCount64 + (long)AnswerAfterUpload.TotalMilliseconds);

        public void Dispose()
        {
            _timer.Dispose();
            _cts.Dispose();
        }
    }

    /// <summary>The file of an upload, telling its <see cref="TransferWatch"/> each time the request reads from it.</summary>
    private sealed class WatchedStream : Stream
    {
        private readonly Stream _inner;
        private readonly TransferWatch _watch;

        public WatchedStream(Stream inner, TransferWatch watch)
        {
            _inner = inner;
            _watch = watch;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Note(_inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Note(await _inner.ReadAsync(buffer, cancellationToken));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Note(await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Note(int read)
        {
            if (read > 0)
            {
                _watch.Moved();
            }
            else
            {
                _watch.Sent();
            }

            return read;
        }
    }

    /// <summary>Sends with the current token, refreshing it once on a 401 that is not « unknown PC ».</summary>
    private async Task<(RelayCallStatus Status, HttpResponseMessage? Response, string? Error)> SendAsync(
        Func<HttpRequestMessage> build, CancellationToken cancellationToken,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead, bool singleAttempt = false,
        HttpClient? via = null)
    {
        var http = via ?? _http;
        for (var attempt = 0; attempt < (singleAttempt ? 1 : 2); attempt++)
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
                response = await http.SendAsync(request, completion, cancellationToken);
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
            if (response.StatusCode == HttpStatusCode.Unauthorized && code is null && attempt == 0 && !singleAttempt)
            {
                response.Dispose();
                continue;
            }

            response.Dispose();
            return (Classify(response.StatusCode, code), null, error);
        }

        return (RelayCallStatus.Unknown, null, RelayRefusals.UnknownRelay);
    }

    private async Task<RelayCall<string>> TokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _token is not null && _utcNow() < _tokenExpiresAtUtc - TimeSpan.FromMinutes(1))
        {
            return new RelayCall<string>(RelayCallStatus.Ok, _token);
        }

        var stale = _token;
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            // Another caller refreshed while this one waited.
            if (_token is not null && !ReferenceEquals(_token, stale) && _utcNow() < _tokenExpiresAtUtc - TimeSpan.FromMinutes(1))
            {
                return new RelayCall<string>(RelayCallStatus.Ok, _token);
            }

            return await RefreshTokenAsync(cancellationToken);
        }
        finally
        {
            _refresh.Release();
        }
    }

    private async Task<RelayCall<string>> RefreshTokenAsync(CancellationToken cancellationToken)
    {

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
        RelayRefusals.RetiredCode => RelayCallStatus.Released,
        RelayRefusals.UnknownRelayCode => RelayCallStatus.Unknown,
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
