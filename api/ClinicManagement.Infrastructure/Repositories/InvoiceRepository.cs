using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Common;
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

    public async Task<PagedResult<Invoice>> GetFilteredAsync(
        Guid clinicId,
        DateTime? from = null,
        DateTime? to = null,
        Guid? patientId = null,
        InvoiceStatus? status = null,
        string? searchTerm = null,
        PageRequest? paging = null,
        Guid? doctorId = null,
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

        // L9 — the practitioner filter, in SQL like every other filter on this read. In the handler it would mean
        // « the ones attributed to her among these 25 », which hides her invoices on every other page — the exact
        // defect `list-pagination` moved the flag and category filters into the repository to remove.
        if (doctorId.HasValue)
        {
            query = query.Where(i => i.DoctorId == doctorId.Value);
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

        // Number or patient name — the two things anyone types when hunting a note d'honoraires.
        //
        // The patient half is an EXISTS against Patients rather than a join, because `Invoice` has no `Patient`
        // navigation (it deliberately holds a bare `PatientId`; see GetPaymentsBetweenAsync). It has to be in
        // SQL all the same: the names are resolved AFTER the page is cut, by the batched id lookup, so a
        // name-based filter applied in the handler could only ever match rows already on the page.
        var pattern = SearchTerm.ToLikePattern(searchTerm);
        if (pattern is not null)
        {
            query = query.Where(i =>
                EF.Functions.ILike(SqlSearch.Unaccent(i.Number)!, pattern, SqlSearch.EscapeString)
                // BOTH name orders: the app renders « Nom Prénom » — see `PatientRepository.ApplySearch`.
                || _context.Patients.Any(p => p.Id == i.PatientId
                    && (EF.Functions.ILike(
                            SqlSearch.Unaccent(p.FirstName + " " + p.LastName)!, pattern, SqlSearch.EscapeString)
                        || EF.Functions.ILike(
                            SqlSearch.Unaccent(p.LastName + " " + p.FirstName)!, pattern, SqlSearch.EscapeString))));
        }

        return await query
            .OrderByDescending(i => i.IssueDate ?? i.CreatedAt)
            .ThenBy(i => i.Id)
            .ToPagedResultAsync(paging, cancellationToken);
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

    public async Task<decimal> GetCollectedBetweenAsync(
        Guid clinicId, DateTime from, DateTime to, Guid? doctorId = null, CancellationToken cancellationToken = default)
    {
        // Voided payments were never really received, so they leave the cash reads entirely — and they leave
        // them on the day the money was recorded, not the day the void happened. A void is a correction: the
        // original day self-corrects to what the clinic actually took. Without this filter the caisse, the
        // dashboard and the revenue KPI would over-report by the voided amount forever.
        // L9 — the practitioner narrowing, and it is a filter on the INVOICE, not on the payment: a payment has no
        // practitioner of its own (whoever took the cash at the desk did not earn the work), so attribution lives on
        // the document the money was collected against.
        return await _context.Invoices
            .Where(i => i.ClinicId == clinicId
                        && i.Status != InvoiceStatus.Cancelled
                        && (doctorId == null || i.DoctorId == doctorId))
            .SelectMany(i => i.Payments)
            .Where(p => !p.IsVoided && p.PaidOn >= from && p.PaidOn <= to)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
    }

    public async Task<decimal> GetInvoicedBetweenAsync(
        Guid clinicId, DateTime from, DateTime toInclusive, Guid? doctorId = null, CancellationToken cancellationToken = default)
    {
        // Same rule as GetInvoiceRevenueQuery's « Total facturé »: only numbered (issued) invoices count, and a
        // cancelled one is void. Dated by IssueDate — a draft has none, which is why the null check is not
        // redundant with the status filter for a legacy row.
        return await _context.Invoices
            .Where(i => i.ClinicId == clinicId
                        && i.Status != InvoiceStatus.Draft
                        && i.Status != InvoiceStatus.Cancelled
                        && i.IssueDate != null
                        && i.IssueDate >= from
                        && i.IssueDate <= toInclusive
                        && (doctorId == null || i.DoctorId == doctorId))
            .SumAsync(i => (decimal?)i.TotalTtc, cancellationToken) ?? 0m;
    }

    public async Task<IReadOnlyList<(Guid PatientId, decimal Outstanding, DateTime? OldestUnpaidIssueDate)>>
        GetOutstandingByPatientAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        // Only issued invoices carry a real balance: drafts aren't billed yet and cancelled ones are void.
        // TTC − collected can't be overpaid (domain guard), so the per-patient sum is always >= 0.
        //
        // The MIN issue date is aggregated in the same projection as the sum (J7) rather than in a second read:
        // both describe the same set of rows, and two queries could disagree about which invoices are unpaid if
        // a payment landed between them. It is the *oldest* because « Retard » is the age of the debt, so the
        // note that has been waiting longest is the one that dates it.
        var rows = await _context.Invoices
            .Where(i => i.ClinicId == clinicId
                        && i.Status != InvoiceStatus.Draft
                        && i.Status != InvoiceStatus.Cancelled
                        && i.TotalTtc > i.AmountCollected)
            .GroupBy(i => i.PatientId)
            .Select(g => new
            {
                PatientId = g.Key,
                Outstanding = g.Sum(i => i.TotalTtc - i.AmountCollected),
                OldestUnpaidIssueDate = g.Min(i => i.IssueDate),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => r.Outstanding > 0m)
            .Select(r => (r.PatientId, r.Outstanding, r.OldestUnpaidIssueDate))
            .ToList();
    }

    public async Task<IReadOnlyList<(
        Guid TreatmentPlanId,
        Guid InvoiceId,
        string? Number,
        InvoiceStatus Status,
        decimal TotalTtc,
        decimal Outstanding)>>
        GetTreatmentPlanLinksAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        // Light projection: a « Facturé » badge, the money-read de-dup and the note's own figures — never the
        // lines/payments graph. Cancelled bridges are returned too; the caller decides if they still count.
        //
        // `Outstanding` is computed here rather than read: it is a derived property on the aggregate, which EF
        // cannot translate, and the two columns it derives from are already in the row.
        var rows = await _context.Invoices
            .Where(i => i.ClinicId == clinicId && i.TreatmentPlanId != null)
            .Select(i => new
            {
                TreatmentPlanId = i.TreatmentPlanId!.Value,
                InvoiceId = i.Id,
                i.Number,
                i.Status,
                i.TotalTtc,
                i.AmountCollected,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => (
                r.TreatmentPlanId,
                r.InvoiceId,
                r.Number,
                r.Status,
                r.TotalTtc,
                Math.Max(0m, r.TotalTtc - r.AmountCollected)))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetPatientIdsByInvoiceIdsAsync(
        Guid clinicId, IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default)
    {
        if (invoiceIds.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        return await _context.Invoices
            .Where(i => i.ClinicId == clinicId && invoiceIds.Contains(i.Id))
            .Select(i => new { i.Id, i.PatientId })
            .ToDictionaryAsync(r => r.Id, r => r.PatientId, cancellationToken);
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

    public async Task<IReadOnlyList<(Guid AppointmentId, Guid InvoiceId, string? Number, InvoiceStatus Status)>>
        GetAppointmentLinksAsync(
            Guid clinicId,
            IReadOnlyCollection<Guid> appointmentIds,
            CancellationToken cancellationToken = default)
    {
        if (appointmentIds.Count == 0)
        {
            return Array.Empty<(Guid, Guid, string?, InvoiceStatus)>();
        }

        // Same light projection as the plan/fiche links, bounded by the caller's id set (see the interface).
        var ids = appointmentIds.Distinct().ToArray();

        var rows = await _context.Invoices
            .Where(i => i.ClinicId == clinicId && i.AppointmentId != null && ids.Contains(i.AppointmentId.Value))
            .Select(i => new { AppointmentId = i.AppointmentId!.Value, InvoiceId = i.Id, i.Number, i.Status })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => (r.AppointmentId, r.InvoiceId, r.Number, r.Status))
            .ToList();
    }

    public async Task<IReadOnlyList<CaissePaymentRow>> GetPaymentsBetweenAsync(
        Guid clinicId, DateTime from, DateTime toInclusive, CancellationToken cancellationToken = default)
    {
        // Predicate-for-predicate the same as GetCollectedBetweenAsync — same clinic scope, same
        // `Status != Cancelled` exclusion, same inclusive bounds — because the statement this feeds must sum to
        // the figure that method returns. The ONE difference is deliberate and documented on the interface:
        // voided rows are NOT filtered here. The sum drops them; the statement shows them struck through.
        var rows = await _context.Invoices
            .Where(i => i.ClinicId == clinicId && i.Status != InvoiceStatus.Cancelled)
            .SelectMany(i => i.Payments
                .Where(p => p.PaidOn >= from && p.PaidOn <= toInclusive)
                .Select(p => new
                {
                    PaymentId = p.Id,
                    InvoiceId = i.Id,
                    InvoiceNumber = i.Number,
                    i.PatientId,
                    p.Amount,
                    p.Method,
                    p.PaidOn,
                    p.IsVoided,
                    p.VoidReason,
                    p.VoidedByName,
                    p.ChequeNumber,
                    p.ChequeBankName,
                    p.ChequeDueDate
                }))
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new CaissePaymentRow(
                r.PaymentId, r.InvoiceId, r.InvoiceNumber, r.PatientId,
                r.Amount, r.Method, r.PaidOn, r.IsVoided, r.VoidReason, r.VoidedByName,
                r.ChequeNumber, r.ChequeBankName, r.ChequeDueDate))
            .ToList();
    }

    public async Task<IReadOnlyList<PaymentMethodTotal>> GetCollectedByMethodBetweenAsync(
        Guid clinicId, DateTime from, DateTime toInclusive, CancellationToken cancellationToken = default)
    {
        // Predicate-for-predicate GetCollectedBetweenAsync with a GROUP BY bolted on — same clinic scope, same
        // `Status != Cancelled`, the same `!IsVoided`, the same inclusive bounds. That identity is the whole
        // point: the breakdown is rendered directly beneath the total, so `Σ breakdown == CashIn` has to be a
        // property of the two queries and not a claim about them.
        var totals = await _context.Invoices
            .Where(i => i.ClinicId == clinicId && i.Status != InvoiceStatus.Cancelled)
            .SelectMany(i => i.Payments)
            .Where(p => !p.IsVoided && p.PaidOn >= from && p.PaidOn <= toInclusive)
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Amount = g.Sum(p => p.Amount) })
            .ToListAsync(cancellationToken);

        return totals.Select(t => new PaymentMethodTotal(t.Method, t.Amount)).ToList();
    }

    public async Task<IReadOnlyList<CaissePaymentRow>> GetChequePaymentsAsync(
        Guid clinicId,
        DateTime? dueFrom = null,
        DateTime? dueTo = null,
        CancellationToken cancellationToken = default)
    {
        // Same projection as GetPaymentsBetweenAsync — one shape for « a payment row with a patient and a
        // document number » — but a different question, so a different predicate: not a period of *receipt* but
        // the set of cheques that still have to reach a bank.
        //
        // A voided cheque is excluded here (unlike the statement, which shows it struck through): the list is a
        // to-do, and a payment that was never really received is not something to go and bank.
        //
        // ⚠️ A **banked** cheque IS returned, and the caller filters. Two reasons it cannot be excluded in SQL:
        // the « Encaissés » view has to be able to show them, and the four bucket counts are over outstanding
        // cheques only (AC-11) — a count derived from an already-filtered set could not tell the two apart.
        //
        // ⚠️ A row with NO due date passes whatever the bounds are. The due date stays optional even for a
        // cheque — refusing money genuinely received to enforce a field is the wrong trade — so the undated
        // cheque is exactly the one nobody will ever chase, and a bounded window that dropped it would hide the
        // case the screen exists for. The caller counts them as their own group.
        var rows = await _context.Invoices
            .Where(i => i.ClinicId == clinicId && i.Status != InvoiceStatus.Cancelled)
            .SelectMany(i => i.Payments
                .Where(p => !p.IsVoided
                            && p.Method == PaymentMethod.Cheque
                            && (p.ChequeDueDate == null
                                || ((dueFrom == null || p.ChequeDueDate >= dueFrom)
                                    && (dueTo == null || p.ChequeDueDate <= dueTo))))
                .Select(p => new
                {
                    PaymentId = p.Id,
                    InvoiceId = i.Id,
                    InvoiceNumber = i.Number,
                    i.PatientId,
                    p.Amount,
                    p.Method,
                    p.PaidOn,
                    p.ChequeNumber,
                    p.ChequeBankName,
                    p.ChequeDueDate,
                    p.ChequeBankedOn,
                    p.ChequeBankedByName
                }))
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new CaissePaymentRow(
                r.PaymentId, r.InvoiceId, r.InvoiceNumber, r.PatientId,
                r.Amount, r.Method, r.PaidOn, IsVoided: false, VoidReason: null, VoidedByName: null,
                r.ChequeNumber, r.ChequeBankName, r.ChequeDueDate,
                r.ChequeBankedOn, r.ChequeBankedByName))
            .ToList();
    }

    public async Task<Invoice?> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        return await _context.Invoices
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Payments.Any(p => p.Id == paymentId), cancellationToken);
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
