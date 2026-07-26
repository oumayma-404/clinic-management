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
}
