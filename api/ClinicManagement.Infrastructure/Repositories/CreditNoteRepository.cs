using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class CreditNoteRepository : ICreditNoteRepository
{
    private readonly ApplicationDbContext _context;

    public CreditNoteRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> GetMaxSequenceForYearAsync(Guid clinicId, int year, CancellationToken cancellationToken = default)
    {
        var prefix = $"{year}-";
        var numbers = await _context.CreditNotes
            .Where(c => c.ClinicId == clinicId && c.Number.StartsWith(prefix))
            .Select(c => c.Number)
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

    public async Task<decimal> GetTotalForInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        return await _context.CreditNotes
            .Where(c => c.InvoiceId == invoiceId)
            .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0m;
    }

    public async Task<decimal> GetRefundedBetweenAsync(Guid clinicId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return await _context.CreditNotes
            .Where(c => c.ClinicId == clinicId && c.RefundedOn >= from && c.RefundedOn <= to)
            .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0m;
    }

    public async Task<CreditNote> AddAsync(CreditNote creditNote, CancellationToken cancellationToken = default)
    {
        await _context.CreditNotes.AddAsync(creditNote, cancellationToken);
        return creditNote;
    }

    public async Task<IReadOnlyList<CreditNote>> GetByInvoiceIdAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        return await _context.CreditNotes
            .AsNoTracking()
            .Where(c => c.InvoiceId == invoiceId)
            .OrderByDescending(c => c.RefundedOn)
            .ThenByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<CreditNote?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.CreditNotes.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<CreditNote>> GetByClinicIdAsync(
        Guid clinicId,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.CreditNotes
            .AsNoTracking()
            .Where(c => c.ClinicId == clinicId);

        if (from.HasValue)
        {
            query = query.Where(c => c.RefundedOn >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(c => c.RefundedOn <= to.Value);
        }

        return await query
            .OrderByDescending(c => c.RefundedOn)
            .ThenByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetTotalsForInvoicesAsync(
        IReadOnlyCollection<Guid> invoiceIds,
        CancellationToken cancellationToken = default)
    {
        if (invoiceIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var rows = await _context.CreditNotes
            .AsNoTracking()
            .Where(c => invoiceIds.Contains(c.InvoiceId))
            .GroupBy(c => c.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Total = g.Sum(c => c.Amount) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.InvoiceId, r => r.Total);
    }
}
