using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Application.Features.Billing.Queries;

/// <summary>
/// The « extrait de caisse » — every movement behind the caisse's totals, oldest first, like a bank statement.
///
/// <para><b>Why a read and not a table.</b> La caisse showed three figures and, underneath them, a table of
/// *expenses only* — so the money-out side was itemised while « Encaissé », the bigger number, was opaque. The
/// obvious fix is a `CashMovement` table every money path writes to. That is double bookkeeping: the day one write
/// site forgets, the statement and the totals disagree and nothing can say which is right. This query reads the
/// <b>same rows the totals sum</b>, which makes « Σ movements == the totals » an assertion a test can hold — and
/// that is the only guarantee worth having on a screen a clinic reconciles its drawer against.</para>
///
/// <para><b>The window is the caller's, deliberately.</b> Same <c>From</c>/<c>To</c> as
/// <see cref="GetCaisseSummaryQuery"/>, defaulting to the same clinic-local day through the same
/// <see cref="ClinicClock.TodayRangeUtc"/>. Any divergence between the two windows would make the statement
/// describe a different period from the totals above it, which is worse than having no statement.</para>
///
/// <para>⚠️ The four reads are awaited <b>sequentially</b>: they share the request's <c>DbContext</c>, so
/// <c>Task.WhenAll</c> throws. Same constraint the dashboard readers document.</para>
/// </summary>
public class GetCaisseLedgerQuery : IRequest<Result<CaisseLedgerDto>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>
    /// 1-based page and page size over the movements. Both null = every movement in the window, which is what the
    /// consistency test (« Σ movements == the totals ») reads.
    /// </summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>
    /// Free-text filter over the movement label, patient, reference and method. Matched in memory — a statement is
    /// the ordered union of four ledgers, so there is no single query to push it into.
    /// </summary>
    public string? SearchTerm { get; set; }
}

public class GetCaisseLedgerQueryHandler : IRequestHandler<GetCaisseLedgerQuery, Result<CaisseLedgerDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetCaisseLedgerQueryHandler> _logger;

    public GetCaisseLedgerQueryHandler(
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository planRepository,
        IExpenseRepository expenseRepository,
        ICreditNoteRepository creditNoteRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetCaisseLedgerQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _planRepository = planRepository;
        _expenseRepository = expenseRepository;
        _creditNoteRepository = creditNoteRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<CaisseLedgerDto>> Handle(GetCaisseLedgerQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
                return Result<CaisseLedgerDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            var clinicId = clinicResult.Value;

            // Bounds resolved exactly as GetCaisseSummaryQuery resolves them — see the type remarks.
            var (todayFrom, todayToInclusive) = ClinicClock.TodayRangeUtc();
            var from = request.From ?? todayFrom;
            var to = request.To ?? (request.From.HasValue ? from.AddDays(1).AddTicks(-1) : todayToInclusive);
            if (to <= from)
                return Result<CaisseLedgerDto>.Failure("La date de fin doit être postérieure à la date de début.");

            // The same de-duplication the totals apply: a devis bridged into an issued invoice has its collections
            // carried onto the invoice, so listing them on the plan side too would show the same money twice.
            var billedPlanIds = PlanBillingRules.BilledPlanIds(
                await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken));

            var payments = await _invoiceRepository.GetPaymentsBetweenAsync(clinicId, from, to, cancellationToken);
            var installmentPayments = await _planRepository.GetInstallmentPaymentsBetweenAsync(
                clinicId, from, to, billedPlanIds, cancellationToken);
            var refunds = await _creditNoteRepository.GetByClinicIdAsync(clinicId, from, to, cancellationToken);
            // Unpaged deliberately: the statement merges these rows with three other ledgers and orders the
            // union, so it cannot be assembled from a page of any one of them.
            var expenses = (await _expenseRepository.GetByClinicIdAsync(
                clinicId, from, to, cancellationToken: cancellationToken)).Items;

            // Names in one pass. Every money-in row carries a patient id and no name — `Invoice` has no `Patient`
            // navigation to project from — so resolving them per row would be an N+1 on the clinic's busiest screen.
            var patientIds = payments.Select(p => p.PatientId)
                .Concat(installmentPayments.Select(p => p.PatientId))
                .Distinct()
                .ToList();
            var patients = await _patientRepository.GetByIdsAsync(clinicId, patientIds, cancellationToken);

            var movements = new List<CaisseMovementDto>();
            movements.AddRange(payments.Select(p => FromInvoicePayment(p, PatientName(patients, p.PatientId))));
            movements.AddRange(installmentPayments.Select(p => FromInstallmentPayment(p, PatientName(patients, p.PatientId))));
            movements.AddRange(refunds.Select(FromRefund));
            movements.AddRange(expenses.Select(FromExpense));

            // Oldest first — a statement reads forward, and the running balance below only means anything in that
            // order. `Kind` then `Id` break ties so two movements on the same date never swap places between two
            // reads of the same window (an unstable statement looks like the data changed). Kind is a name rather
            // than the enum, so the tie-break is alphabetical — arbitrary but stable, which is all it must be.
            var ordered = movements
                .OrderBy(m => m.OccurredOn)
                .ThenBy(m => m.Kind)
                .ThenBy(m => m.Id)
                .ToList();

            // The running balance is computed over the WHOLE window, before any filtering or paging. That order
            // is load-bearing: « Solde de la période » means "where the till stood after this movement", which is
            // a fact about the movement's position in the window and not about which page it happens to land on.
            // Computing it after a filter would print a column that adds up to nothing.
            var balance = 0m;
            foreach (var movement in ordered)
            {
                // A voided row is shown and does not move the balance — it was never really received.
                if (!movement.IsVoided)
                {
                    balance += movement.Direction == nameof(CaisseMovementDirection.In)
                        ? movement.Amount
                        : -movement.Amount;
                }
                movement.RunningBalance = InvoiceCalculator.RoundMoney(balance);
            }

            // Filtered and paged in memory for the same reason as « Créances »: a statement is the ordered union
            // of four ledgers, so no single query knows a row's position in it. Unlike the other lists this one is
            // already bounded by its date window — the paging is here so a month-long extrait does not render
            // thousands of rows at once, not because the read was unbounded.
            var visible = string.IsNullOrWhiteSpace(request.SearchTerm)
                ? ordered
                : ordered
                    .Where(m => SearchTerm.Matches(request.SearchTerm, m.Label, m.PatientName, m.Reference, m.Method))
                    .ToList();

            var page = PagedResult<CaisseMovementDto>.FromSource(
                visible, PageRequest.From(request.Page, request.PageSize));

            return Result<CaisseLedgerDto>.Success(new CaisseLedgerDto
            {
                FromDate = from,
                ToDate = to,
                Movements = page.Items.ToList(),
                Page = page.Page,
                PageSize = page.PageSize,
                TotalCount = page.TotalCount,
                TotalPages = page.TotalPages
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error building the caisse ledger");
            return Result<CaisseLedgerDto>.Failure("Erreur lors du calcul de l'extrait de caisse.");
        }
    }

    private static string? PatientName(IReadOnlyDictionary<Guid, Patient> patients, Guid patientId) =>
        patients.TryGetValue(patientId, out var patient) ? patient.GetFullName() : null;

    private static CaisseMovementDto FromInvoicePayment(CaissePaymentRow row, string? patientName) => new()
    {
        Id = row.PaymentId,
        Kind = nameof(CaisseMovementKind.InvoicePayment),
        Direction = nameof(CaisseMovementDirection.In),
        OccurredOn = row.PaidOn,
        Amount = InvoiceCalculator.RoundMoney(row.Amount),
        Method = row.Method.ToString(),
        // A draft invoice has no number yet, so the label says what the movement is rather than printing « n°  ».
        Label = row.InvoiceNumber is null
            ? "Paiement — facture en brouillon"
            : $"Paiement facture {row.InvoiceNumber}",
        Reference = row.InvoiceNumber,
        PatientId = row.PatientId,
        PatientName = patientName,
        TargetId = row.InvoiceId,
        IsVoided = row.IsVoided,
        VoidReason = row.VoidReason,
        VoidedByName = row.VoidedByName
    };

    private static CaisseMovementDto FromInstallmentPayment(CaisseInstallmentPaymentRow row, string? patientName) => new()
    {
        Id = row.PaymentId,
        Kind = nameof(CaisseMovementKind.InstallmentPayment),
        Direction = nameof(CaisseMovementDirection.In),
        OccurredOn = row.PaidOn,
        Amount = InvoiceCalculator.RoundMoney(row.Amount),
        Method = row.Method.ToString(),
        Label = row.PlanNumber is null
            ? "Échéance — devis sans numéro"
            : $"Échéance devis {row.PlanNumber}",
        Reference = row.PlanNumber,
        PatientId = row.PatientId,
        PatientName = patientName,
        TargetId = row.TreatmentPlanId,
        IsVoided = row.IsVoided,
        VoidReason = row.VoidReason,
        VoidedByName = row.VoidedByName
    };

    private static CaisseMovementDto FromRefund(CreditNote note) => new()
    {
        Id = note.Id,
        Kind = nameof(CaisseMovementKind.Refund),
        Direction = nameof(CaisseMovementDirection.Out),
        OccurredOn = note.RefundedOn,
        Amount = InvoiceCalculator.RoundMoney(note.Amount),
        // An avoir's method is nullable — a legacy row may have none recorded.
        Method = note.Method?.ToString(),
        Label = $"Avoir {note.Number} — {note.Reason}",
        Reference = note.Number,
        // The avoir belongs to an invoice, not directly to a patient, and CreditNote carries no PatientId. Opening
        // the invoice is the useful destination anyway: that is where the avoir is listed and printable.
        TargetId = note.InvoiceId,
        // An avoir is itself the reversal of a payment; there is no void-an-avoir path, so these stay false/null.
        IsVoided = false
    };

    private static CaisseMovementDto FromExpense(Expense expense) => new()
    {
        Id = expense.Id,
        Kind = nameof(CaisseMovementKind.Expense),
        Direction = nameof(CaisseMovementDirection.Out),
        OccurredOn = expense.ExpenseDate,
        Amount = InvoiceCalculator.RoundMoney(expense.Amount),
        Method = expense.Method.ToString(),
        Label = string.IsNullOrWhiteSpace(expense.Description)
            ? $"Dépense — {expense.Category}"
            : $"Dépense — {expense.Category} · {expense.Description}",
        Reference = null,
        TargetId = expense.Id,
        IsVoided = false
    };
}
