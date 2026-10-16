using ClinicManagement.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

public enum IdempotencyClaimOutcome
{
    /// <summary>The key is this request's: run it.</summary>
    Claimed,
    /// <summary>The key's save already succeeded: answer what it answered.</summary>
    Replay,
    /// <summary>The same save is still running (a double-click, a re-press while the first is in flight).</summary>
    InProgress,
    /// <summary>The key was first used on another request: a client bug, never a second save.</summary>
    Mismatch,
}

public sealed record IdempotencyClaim(IdempotencyClaimOutcome Outcome, int? StatusCode = null, string? ContentType = null, string? Body = null);

/// <summary>The replay store behind D17 — one row per (cabinet, key), claimed before the save runs.</summary>
public interface IIdempotencyStore
{
    Task<IdempotencyClaim> ClaimAsync(Guid clinicId, string key, string fingerprint, DateTime nowUtc, CancellationToken cancellationToken);

    Task CompleteAsync(Guid clinicId, string key, int statusCode, string? contentType, string body, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>The save failed or its answer cannot be kept: the key is free again, so a re-press runs it again.</summary>
    Task ReleaseAsync(Guid clinicId, string key, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IIdempotencyStore"/> over the request's own <see cref="ApplicationDbContext"/>, in raw SQL: a claim must
/// be committed before the save starts and survive the save's rollback, so it is never part of a <c>SaveChanges</c> —
/// which also keeps it out of the change log, the audit chain and the lease's net.
/// </summary>
public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly ApplicationDbContext _db;

    public IdempotencyStore(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IdempotencyClaim> ClaimAsync(
        Guid clinicId, string key, string fingerprint, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var expired = nowUtc - IdempotencyRecord.Retention;
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"DELETE FROM ""IdempotencyRecords"" WHERE ""ClinicId"" = {clinicId} AND ""CreatedAtUtc"" < {expired}",
            cancellationToken);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var inserted = await _db.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO ""IdempotencyRecords"" (""ClinicId"", ""Key"", ""Fingerprint"", ""State"", ""CreatedAtUtc"")
                   VALUES ({clinicId}, {key}, {fingerprint}, {(int)IdempotencyRecordState.InProgress}, {nowUtc})
                   ON CONFLICT (""ClinicId"", ""Key"") DO NOTHING",
                cancellationToken);
            if (inserted == 1)
            {
                return new IdempotencyClaim(IdempotencyClaimOutcome.Claimed);
            }

            var row = await _db.Set<IdempotencyRecord>().IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.ClinicId == clinicId && r.Key == key)
                .Select(r => new { r.Fingerprint, r.State, r.StatusCode, r.ContentType, r.Body, r.CreatedAtUtc })
                .FirstOrDefaultAsync(cancellationToken);
            if (row is null)
            {
                continue; // released between the insert and the read: claim it now
            }

            if (!string.Equals(row.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return new IdempotencyClaim(IdempotencyClaimOutcome.Mismatch);
            }

            if (row.State == IdempotencyRecordState.Completed)
            {
                return new IdempotencyClaim(IdempotencyClaimOutcome.Replay, row.StatusCode, row.ContentType, row.Body);
            }

            // A request that died mid-save never released its key: past the timeout it is claimed again.
            if (row.CreatedAtUtc < nowUtc - IdempotencyRecord.InProgressTimeout)
            {
                var taken = await _db.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE ""IdempotencyRecords"" SET ""CreatedAtUtc"" = {nowUtc}
                       WHERE ""ClinicId"" = {clinicId} AND ""Key"" = {key}
                         AND ""State"" = {(int)IdempotencyRecordState.InProgress} AND ""CreatedAtUtc"" = {row.CreatedAtUtc}",
                    cancellationToken);
                if (taken == 1)
                {
                    return new IdempotencyClaim(IdempotencyClaimOutcome.Claimed);
                }
            }

            return new IdempotencyClaim(IdempotencyClaimOutcome.InProgress);
        }

        return new IdempotencyClaim(IdempotencyClaimOutcome.InProgress);
    }

    public Task CompleteAsync(
        Guid clinicId, string key, int statusCode, string? contentType, string body, DateTime nowUtc,
        CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE ""IdempotencyRecords""
               SET ""State"" = {(int)IdempotencyRecordState.Completed}, ""StatusCode"" = {statusCode},
                   ""ContentType"" = {contentType}, ""Body"" = {body}, ""CompletedAtUtc"" = {nowUtc}
               WHERE ""ClinicId"" = {clinicId} AND ""Key"" = {key}",
            cancellationToken);

    public Task ReleaseAsync(Guid clinicId, string key, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlInterpolatedAsync(
            $@"DELETE FROM ""IdempotencyRecords""
               WHERE ""ClinicId"" = {clinicId} AND ""Key"" = {key} AND ""State"" = {(int)IdempotencyRecordState.InProgress}",
            cancellationToken);
}

/// <summary>
/// The key the API's middleware accepted for this request (<c>HttpContext.Items</c>), recorded on every change the
/// request's saves make (D17). Null outside a request and for a request that carried none.
/// </summary>
public sealed class HttpIdempotencyKeyAccessor : IIdempotencyKeyAccessor
{
    public const string ItemsKey = "clinic.idempotency-key";

    private readonly IHttpContextAccessor? _http;

    public HttpIdempotencyKeyAccessor(IHttpContextAccessor? http)
    {
        _http = http;
    }

    public string? Current => _http?.HttpContext?.Items[ItemsKey] as string;
}
