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

    /// <summary>Above this the installer comes in pieces over several connections (one ran at ~100 KB/s, London to Tunis).</summary>
    public const long ParallelAbove = 32L * 1024 * 1024;

    public const int PieceBytes = 8 * 1024 * 1024;
    public const int Connections = 6;

    private readonly HttpClient _http;
    private readonly long _parallelAbove;
    private readonly int _pieceBytes;

    public RelayInstallerDownloader(HttpClient http, Uri apiBase, long parallelAbove = ParallelAbove, int pieceBytes = PieceBytes)
    {
        _http = http;
        _parallelAbove = parallelAbove;
        _pieceBytes = pieceBytes;
        _http.BaseAddress ??= apiBase;
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<RelayInstallerFetch> FetchAsync(string partPath, string build, CancellationToken cancellationToken)
    {
        // A download already going in pieces carries on in pieces, from the pieces it has.
        if (PieceMap.Load(MapPath(partPath)) is { } resumed)
        {
            if (resumed.Build == build && File.Exists(partPath))
            {
                return await InPiecesAsync(partPath, resumed, cancellationToken);
            }

            DeletePieces(partPath);
        }

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

            // A whole answer for a big file: drop it and fetch the file in pieces, several at a time.
            if (offset == 0 && response.StatusCode == HttpStatusCode.OK
                && response.Content.Headers.ContentLength is { } length && length > _parallelAbove
                && response.Headers.AcceptRanges.Contains("bytes"))
            {
                var map = new PieceMap(build, sha256, length, _pieceBytes, new SortedSet<int>());
                await using (var sized = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    sized.SetLength(length);
                }

                map.Save(MapPath(partPath));
                response.Dispose();
                return await InPiecesAsync(partPath, map, cancellationToken);
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

    private static string MapPath(string partPath) => partPath + ".pieces";

    private static void DeletePieces(string partPath)
    {
        try
        {
            File.Delete(MapPath(partPath));
            File.Delete(partPath);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// The pieces still missing, fetched by <see cref="Connections"/> workers sharing one queue — a fast connection takes
    /// the next piece instead of waiting for a slow one. Each finished piece is noted on disk, so a cut, a restart or the
    /// next tick resumes from the pieces it has. The hash, checked by the updater, still decides.
    /// </summary>
    private async Task<RelayInstallerFetch> InPiecesAsync(string partPath, PieceMap map, CancellationToken cancellationToken)
    {
        var missing = new System.Collections.Concurrent.ConcurrentQueue<int>(
            Enumerable.Range(0, map.Count).Where(i => !map.Done.Contains(i)));
        var gate = new object();
        string? otherBuild = null;
        string? error = null;

        async Task WorkAsync()
        {
            while (otherBuild is null && missing.TryDequeue(out var piece))
            {
                var from = (long)piece * map.PieceBytes;
                var to = Math.Min(map.Length, from + map.PieceBytes) - 1;
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stall.CancelAfter(StallAfter);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, Route);
                    request.Headers.Range = new RangeHeaderValue(from, to);
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                    var served = Header(response, BuildHeader);
                    if (served is not null && !string.Equals(served, map.Build, StringComparison.Ordinal))
                    {
                        otherBuild = served;
                        return;
                    }

                    if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != from)
                    {
                        error = $"piece {piece}: HTTP {(int)response.StatusCode}";
                        return;
                    }

                    await using var body = await response.Content.ReadAsStreamAsync(stall.Token);
                    await using var file = new FileStream(partPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 81920, useAsync: true);
                    file.Seek(from, SeekOrigin.Begin);
                    var buffer = new byte[81920];
                    var position = from;
                    int read;
                    while (position <= to && (read = await body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, to - position + 1)), stall.Token)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), CancellationToken.None);
                        position += read;
                        stall.CancelAfter(StallAfter);
                    }

                    if (position <= to)
                    {
                        error = $"piece {piece}: cut short";
                        return;
                    }

                    lock (gate)
                    {
                        map.Done.Add(piece);
                        try
                        {
                            map.Save(MapPath(partPath));
                        }
                        catch (IOException)
                        {
                            // The note is only a head start for a resume: a piece it misses is fetched again.
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    error = cancellationToken.IsCancellationRequested ? "stopped" : "stalled";
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
                {
                    error = ex.Message;
                    return;
                }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Connections).Select(_ => WorkAsync()));

        if (otherBuild is not null)
        {
            DeletePieces(partPath);
            return new RelayInstallerFetch(RelayInstallerFetchStatus.OtherBuild, Error: otherBuild);
        }

        if (map.Done.Count < map.Count)
        {
            return new RelayInstallerFetch(RelayInstallerFetchStatus.Interrupted, Error: error ?? "pieces missing");
        }

        File.Delete(MapPath(partPath));
        return new RelayInstallerFetch(RelayInstallerFetchStatus.Complete, map.Sha256);
    }

    /// <summary>What a download in pieces has: its build, hash, length and the pieces already on disk.</summary>
    private sealed record PieceMap(string Build, string Sha256, long Length, int PieceBytes, SortedSet<int> Done)
    {
        public int Count => (int)((Length + PieceBytes - 1) / PieceBytes);

        public void Save(string path)
        {
            // Written in place (a rename over it can be refused while Windows' scanner holds it); a torn note fails to
            // parse and the download starts afresh, which is safe — the hash still decides.
            File.WriteAllLines(path, new[] { Build, Sha256, Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PieceBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Join(',', Done) });
        }

        public static PieceMap? Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var lines = File.ReadAllLines(path);
                var done = new SortedSet<int>(lines.Length > 4 && lines[4].Length > 0
                    ? lines[4].Split(',').Select(int.Parse)
                    : Enumerable.Empty<int>());
                return new PieceMap(lines[0], lines[1], long.Parse(lines[2], System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(lines[3], System.Globalization.CultureInfo.InvariantCulture), done);
            }
            catch (Exception ex) when (ex is IOException or FormatException or IndexOutOfRangeException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault()?.Trim() : null;
}
