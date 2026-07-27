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



