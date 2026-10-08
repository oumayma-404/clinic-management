using System.Net;
using System.Net.Http.Headers;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>
/// Fetches the cloud's installer for the PC de secours's self-update (D10b), resuming a part file with <c>Range</c>
/// across cuts and restarts. Anonymous, like the Windows app's own download: the installer is the product every
/// cabinet gets, and what makes it trustworthy is the hash beside it, checked before anything runs.
///
/// <para>⚠️ The route and both header names are copies of <c>RelayPeerController</c>'s (Infrastructure references no
/// API assembly); a test holds them equal, since a renamed header would leave every PC refusing every installer.</para>
/// </summary>
public sealed class RelayInstallerDownloader : IRelayInstallerSource
{
    public const string Route = "relay/installer";
    public const string BuildHeader = "X-Relay-Build";
    public const string Sha256Header = "X-Content-SHA256";

    /// <summary>No byte for this long and the attempt ends — a slow line is fine, a silent one is not.</summary>
    public static readonly TimeSpan StallAfter = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;

    public RelayInstallerDownloader(HttpClient http, Uri apiBase)
    {
        _http = http;
        _http.BaseAddress ??= apiBase;
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<RelayInstallerFetch> FetchAsync(string partPath, string build, CancellationToken cancellationToken)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(StallAfter);
        try
        {
            var offset = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
            using var request = new HttpRequestMessage(HttpMethod.Get, Route);
            if (offset > 0)
            {
                request.Headers.Range = new RangeHeaderValue(offset, null);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new RelayInstallerFetch(RelayInstallerFetchStatus.NotPublished);
            }

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    // What is on disk is not a prefix of what the cloud serves now: start again from the first byte.
                    File.Delete(partPath);
                }

                return new RelayInstallerFetch(RelayInstallerFetchStatus.Interrupted, Error: $"HTTP {(int)response.StatusCode}");
            }

            var served = Header(response, BuildHeader);
            if (!string.Equals(served, build, StringComparison.Ordinal))
            {
                return new RelayInstallerFetch(RelayInstallerFetchStatus.OtherBuild, Error: served);
            }

            var sha256 = Header(response, Sha256Header);
            if (sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit))
            {
                return new RelayInstallerFetch(RelayInstallerFetchStatus.Interrupted, Error: "no installer hash");
            }

            FileMode mode;
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                if (response.Content.Headers.ContentRange?.From != offset)
                {
                    File.Delete(partPath);
                    return new RelayInstallerFetch(RelayInstallerFetchStatus.Interrupted, Error: "unexpected range");
                }

                mode = FileMode.Append;
            }
            else
            {
                // A whole answer — a first attempt, or a cloud that ignored the range: what is on disk goes.
                mode = FileMode.Create;
            }

            await using var file = new FileStream(partPath, mode, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await using var body = await response.Content.ReadAsStreamAsync(stall.Token);
            var buffer = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(buffer, stall.Token)) > 0)
            {
                // Never cancelled mid-write: a part file must only ever hold whole buffers the cloud sent.
                await file.WriteAsync(buffer.AsMemory(0, read), CancellationToken.None);
                stall.CancelAfter(StallAfter);
            }

            return new RelayInstallerFetch(RelayInstallerFetchStatus.Complete, sha256);
        }
        catch (OperationCanceledException)
        {
            return new RelayInstallerFetch(RelayInstallerFetchStatus.Interrupted,
                Error: cancellationToken.IsCancellationRequested ? "stopped" : "stalled");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return new RelayInstallerFetch(RelayInstallerFetchStatus.Interrupted, Error: ex.Message);
        }
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault()?.Trim() : null;
}
