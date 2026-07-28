using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _transaction;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// The single place a concurrency conflict is translated. Every write in the app funnels through here, so
    /// putting the translation at the seam means no handler has to know about EF's exception type — they only
    /// need to stop swallowing <see cref="ConflictException"/>.
    /// </summary>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // EF's UPDATE carried the xmin we read; matching zero rows means a peer wrote the row first.
            // The tracked entities are left as they are: recovery is a reload, and silently re-applying the
            // caller's values over the winner's is exactly the last-write-wins behaviour being removed.
            throw new ConflictException(ErrorMessages.Conflict, ex);
        }
        // MUST stay below the DbUpdateConcurrencyException arm — that type derives from DbUpdateException, so
        // ordering this first would swallow every concurrency conflict into the wrong message.
        catch (DbUpdateException ex) when (IsExclusionViolation(ex))
        {
            // AC-P1.18: PostgreSQL 23P01 — the appointment exclusion constraint refused an overlapping booking.
            // § 1 translated only DbUpdateConcurrencyException, so the loser of a genuine double-booking race
            // received a raw 500. Translated here, at the same seam, so no handler has to know the SQLSTATE:
            // it surfaces as a 409 with a French message instead.
            //
            // The application guard in AppointmentScheduling still runs first and produces a message naming the
            // clashing slot — this is the backstop for the narrow window between that check and the insert,
            // which is the whole reason the constraint exists (a check-then-insert cannot be made safe by
            // widening the check).
            throw new ConflictException(ErrorMessages.SlotAlreadyBooked, ex);
        }
    }

    /// <summary>
    /// True when the failure is PostgreSQL's exclusion-constraint violation (SQLSTATE <c>23P01</c>).
    /// <para>
    /// Matched on the <b>type name</b> rather than by casting to <c>PostgresException</c>, following the
    /// precedent in <c>StartupDiagnostics</c>: it keeps this seam from taking a hard compile-time dependency on
    /// an Npgsql type, so the translation is provider-shaped without being provider-bound.
    /// </para>
    /// </summary>
    private static bool IsExclusionViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
        {
            if (inner.GetType().FullName != "Npgsql.PostgresException")
            {
                continue;
            }

            var sqlState = inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string;
            if (sqlState == "23P01")
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void SetExpectedVersion(object entity, uint expectedVersion)
    {
        if (expectedVersion == 0)
        {
            return;
        }

        var entry = _context.Entry(entity);
        if (entry.State == EntityState.Detached)
        {
            return;
        }

        // OriginalValue — not CurrentValue — is what EF puts in the UPDATE's WHERE clause. Setting the
        // current value here would change what we write, not what we check, and detect nothing.
        entry.Property(nameof(ClinicManagement.Domain.Common.Entity<int>.Version)).OriginalValue = expectedVersion;
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}



