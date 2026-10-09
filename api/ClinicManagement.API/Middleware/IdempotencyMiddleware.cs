using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// <c>clinic-pc-copy</c> D17, FR-6: a cabinet's save carries an <c>Idempotency-Key</c>, and pressed again after its
/// answer was lost it is <b>answered again, never recorded twice</b>. The key is claimed before the save runs (two
/// presses of one save meet on the claim), its 2xx answer is kept 48 h and replayed, and anything else frees the key.
///
/// <para>⚠️ <b>Before the lease and subscription gates, after the token checks</b>: a save that went through before
/// the cloud locked must still be answered « fait » when pressed again during the lock, and a revoked token must never
/// replay anything. ⚠️ Only a cabinet's own writes: no clinic in scope (sign-in, the vendor console, a PC's channel)
/// passes untouched. ⚠️ Every key the request's saves make is stamped on the change log (D17), which is what the
/// return compares — the replay store itself stays on its own side.</para>
/// </summary>
public sealed class IdempotencyMiddleware
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotency-Replayed";
    public const string InProgressCode = "idempotency_in_progress";
    public const string KeyReusedCode = "idempotency_key_reused";
    public const string InProgressMessage = "Cet enregistrement est déjà en cours d'envoi. Patientez un instant.";
    public const string KeyReusedMessage = "Cette demande a déjà servi pour un autre enregistrement. Rechargez la page.";

    /// <summary>A larger answer is not kept: the key is freed and a re-press runs the save again.</summary>
    public const int MaxStoredBodyBytes = 256 * 1024;

    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantScope tenantScope, IIdempotencyStore store)
    {
        if (!Applies(context, tenantScope, out var key, out var clinicId))
        {
            await _next(context);
            return;
        }

        var fingerprint = Fingerprint(context.Request);
        var claim = await store.ClaimAsync(clinicId, key, fingerprint, DateTime.UtcNow, context.RequestAborted);
        switch (claim.Outcome)
        {
            case IdempotencyClaimOutcome.Replay:
                await ReplayAsync(context, claim);
                return;
            case IdempotencyClaimOutcome.InProgress:
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new { error = InProgressMessage, code = InProgressCode });
                return;
            case IdempotencyClaimOutcome.Mismatch:
                context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                await context.Response.WriteAsJsonAsync(new { error = KeyReusedMessage, code = KeyReusedCode });
                return;
        }

        context.Items[HttpIdempotencyKeyAccessor.ItemsKey] = key;
        var original = context.Response.Body;
        var capture = new CapturingStream(original, MaxStoredBodyBytes);
        context.Response.Body = capture;
        var kept = false;
        try
        {
            await _next(context);
            // A writer that went through the response's pipe may still hold bytes: they reach the capture (and the
            // client) before the body is swapped back, or the kept answer — and the client's — would be cut short.
            await context.Response.BodyWriter.FlushAsync(context.RequestAborted);

            var status = context.Response.StatusCode;
            if (status is >= 200 and < 300 && !capture.Overflowed && IsReplayable(context.Response.ContentType, capture.Length))
            {
                await store.CompleteAsync(clinicId, key, status, context.Response.ContentType, capture.Text(),
                    DateTime.UtcNow, CancellationToken.None);
                kept = true;
            }
        }
        finally
        {
            context.Response.Body = original;
            if (!kept)
            {
                try
                {
                    await store.ReleaseAsync(clinicId, key, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    // A key left claimed is freed by its timeout; the save's own outcome stands.
                    _logger.LogWarning(ex, "Could not free idempotency key for clinic {ClinicId}", clinicId);
                }
            }
        }
    }

    public static bool Applies(HttpContext context, ITenantScope tenantScope, out string key, out Guid clinicId)
    {
        key = string.Empty;
        clinicId = Guid.Empty;
        var request = context.Request;
        if (!request.Path.StartsWithSegments("/api")
            || HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method)
            || tenantScope.Kind != TenantScopeKind.Clinic || tenantScope.ClinicId is not { } scoped)
        {
            return false;
        }

        var header = request.Headers[HeaderName].ToString().Trim();
        if (!IdempotencyRecord.IsValidKey(header))
        {
            return false;
        }

        key = header;
        clinicId = scoped;
        return true;
    }

    /// <summary>The method and path (with its query) — hashed past the column's width so a long query still fits.</summary>
    public static string Fingerprint(HttpRequest request)
    {
        var text = $"{request.Method.ToUpperInvariant()} {request.Path}{request.QueryString}";
        return text.Length <= IdempotencyRecord.MaxFingerprintLength
            ? text
            : "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>A JSON answer, or none at all (201 / 204): what a save answers. A file is never kept — it is rebuilt.</summary>
    public static bool IsReplayable(string? contentType, long length) =>
        length == 0 || (contentType is not null && contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase));

    private static async Task ReplayAsync(HttpContext context, IdempotencyClaim claim)
    {
        context.Response.StatusCode = claim.StatusCode ?? StatusCodes.Status200OK;
        context.Response.Headers[ReplayedHeader] = "true";
        if (!string.IsNullOrEmpty(claim.Body))
        {
            context.Response.ContentType = claim.ContentType ?? "application/json; charset=utf-8";
            await context.Response.WriteAsync(claim.Body, Encoding.UTF8);
        }
    }

    /// <summary>Writes through to the response and keeps a copy of the first bytes — never delays what the client gets.</summary>
    private sealed class CapturingStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _cap;
        private readonly MemoryStream _copy = new();

        public CapturingStream(Stream inner, int cap)
        {
            _inner = inner;
            _cap = cap;
        }

        public bool Overflowed { get; private set; }
        public override long Length => _copy.Length + (Overflowed ? 1 : 0);
        public string Text() => Encoding.UTF8.GetString(_copy.GetBuffer(), 0, (int)_copy.Length);

        private void Keep(ReadOnlySpan<byte> buffer)
        {
            if (Overflowed)
            {
                return;
            }

            if (_copy.Length + buffer.Length > _cap)
            {
                Overflowed = true;
                return;
            }

            _copy.Write(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Keep(buffer.AsSpan(offset, count));
            _inner.Write(buffer, offset, count);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Keep(buffer.AsSpan(offset, count));
            await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Keep(buffer.Span);
            await _inner.WriteAsync(buffer, cancellationToken);
        }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Position { get => _copy.Length; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
