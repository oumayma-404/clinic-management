using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class InvoiceRepository : IInvoiceRepository
{
    private readonly ApplicationDbContext _context;

    public InvoiceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Invoices
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Invoice>> GetFilteredAsync(
        Guid clinicId,
        DateTime? from = null,
        DateTime? to = null,
        Guid? patientId = null,
        InvoiceStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Invoices
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .Where(i => i.ClinicId == clinicId);

        if (patientId.HasValue)
        {
            query = query.Where(i => i.PatientId == patientId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        // Date range applies to the issue date; drafts (no issue date) are excluded from a ranged query.
        if (from.HasValue)
        {
            query = query.Where(i => i.IssueDate != null && i.IssueDate >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(i => i.IssueDate != null && i.IssueDate <= to.Value);
        }

        return await query
            .OrderByDescending(i => i.IssueDate ?? i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetMaxSequenceForYearAsync(Guid clinicId, int year, CancellationToken cancellationToken = default)
    {
        var prefix = $"{year}-";

        // Pull the assigned numbers for this clinic+year and parse the sequence part in memory. The row
        // count per clinic per year is small, and this avoids relying on DB string-splitting.
        var numbers = await _context.Invoices
            .Where(i => i.ClinicId == clinicId && i.Number != null && i.Number.StartsWith(prefix))
            .Select(i => i.Number!)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var number in numbers)
        {
            var dashIndex = number.LastIndexOf('-');
            if (dashIndex >= 0 && int.TryParse(number[(dashIndex + 1)..], out var sequence) && sequence > max)
            {
                max = sequence;
            }
        }

        return max;
    }

    public async Task<decimal> GetCollectedBetweenAsync(Guid clinicId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return await _context.Invoices
            .Where(i => i.ClinicId == clinicId && i.Status != InvoiceStatus.Cancelled)
            .SelectMany(i => i.Payments)
            .Where(p => p.PaidOn >= from && p.PaidOn <= to)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
    }

    public async Task<Invoice> AddAsync(Invoice invoice, CancellationToken cancellationToken = default)
    {
        await _context.Invoices.AddAsync(invoice, cancellationToken);
        return invoice;
    }

    public Task UpdateAsync(Invoice invoice, CancellationToken cancellationToken = default)
    {
        // Handlers load the invoice tracked (GetByIdAsync) and mutate the aggregate, so EF change
        // tracking persists lines/payments adds & removals on SaveChanges. Only attach if detached.
        var entry = _context.Entry(invoice);
        if (entry.State == EntityState.Detached)
        {
            _context.Invoices.Update(invoice);
        }
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (invoice != null)
        {
            _context.Invoices.Remove(invoice);
        }
    }
}
