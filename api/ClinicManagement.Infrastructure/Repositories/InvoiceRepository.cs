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
        // Voided payments were never really received, so they leave the cash reads entirely — and they leave
        // them on the day the money was recorded, not the day the void happened. A void is a correction: the
        // original day self-corrects to what the clinic actually took. Without this filter the caisse, the
        // dashboard and the revenue KPI would over-report by the voided amount forever.
        return await _context.Invoices
            .Where(i => i.ClinicId == clinicId && i.Status != InvoiceStatus.Cancelled)
            .SelectMany(i => i.Payments)
            .Where(p => !p.IsVoided && p.PaidOn >= from && p.PaidOn <= to)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
    }

    public async Task<IReadOnlyList<(Guid PatientId, decimal Outstanding)>> GetOutstandingByPatientAsync(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        // Only issued invoices carry a real balance: drafts aren't billed yet and cancelled ones are void.
        // TTC − collected can't be overpaid (domain guard), so the per-patient sum is always >= 0.
        var rows = await _context.Invoices
            .Where(i => i.ClinicId == clinicId
                        && i.Status != InvoiceStatus.Draft
                        && i.Status != InvoiceStatus.Cancelled
                        && i.TotalTtc > i.AmountCollected)
            .GroupBy(i => i.PatientId)
            .Select(g => new { PatientId = g.Key, Outstanding = g.Sum(i => i.TotalTtc - i.AmountCollected) })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => r.Outstanding > 0m)
            .Select(r => (r.PatientId, r.Outstanding))
            .ToList();
    }

    public async Task<IReadOnlyList<(Guid TreatmentPlanId, Guid InvoiceId, string? Number, InvoiceStatus Status)>>
        GetTreatmentPlanLinksAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        // Light projection: a « Facturé » badge and the money-read de-dup need the link + status, never the
        // lines/payments graph. Cancelled bridges are returned too — the caller decides if they still count.
        var rows = await _context.Invoices
            .Where(i => i.ClinicId == clinicId && i.TreatmentPlanId != null)
            .Select(i => new { TreatmentPlanId = i.TreatmentPlanId!.Value, InvoiceId = i.Id, i.Number, i.Status })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => (r.TreatmentPlanId, r.InvoiceId, r.Number, r.Status))
            .ToList();
    }

    public async Task<IReadOnlyList<(Guid DentalRecordId, Guid InvoiceId, string? Number, InvoiceStatus Status)>>
        GetDentalRecordLinksAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        // Same shape and the same reasoning as GetTreatmentPlanLinksAsync, one level down: the question is
        // "which fiches are billed", and answering it via GetFilteredAsync would drag every line and payment
        // of every invoice along with it. SelectMany over the lines keeps it a single projected read.
        var rows = await _context.Invoices
            .Where(i => i.ClinicId == clinicId)
            .SelectMany(i => i.Lines
                .Where(l => l.DentalRecordId != null)
                .Select(l => new
                {
                    DentalRecordId = l.DentalRecordId!.Value,
                    InvoiceId = i.Id,
                    i.Number,
                    i.Status
                }))
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => (r.DentalRecordId, r.InvoiceId, r.Number, r.Status))
            .ToList();
    }

    public async Task<Invoice?> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        return await _context.Invoices
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Payments.Any(p => p.Id == paymentId), cancellationToken);
    }

    public async Task<IEnumerable<Invoice>> GetDueForElFatooraDispatchAsync(int maxCount, DateTime now, CancellationToken cancellationToken = default)
    {
        return await _context.Invoices
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .Where(i => i.EInvoiceStatus == EInvoiceStatus.Queued
                        && i.EInvoiceNextAttemptAt != null
                        && i.EInvoiceNextAttemptAt <= now)
            .OrderBy(i => i.EInvoiceNextAttemptAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
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
