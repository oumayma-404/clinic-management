using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Billing.Queries;

/// <summary>
/// « Chèques à encaisser » — the payoff of L8's cheque fields, and the answer to a question the product could not
/// previously be asked: <i>which cheques am I still holding, and which of them can go to the bank today?</i>
///
/// <para><b>Both ledgers, one list.</b> A cheque is received either against a note d'honoraires
/// (<c>Payment</c>) or against a devis échéance (<c>InstallmentPayment</c>) — the second being the archetypal
/// Tunisian case, a book of post-dated cheques for an échéancier — so a screen covering one of them would be a
/// screen that hides half the money. Same merge-two-ledgers shape as the « extrait de caisse » and « Créances »,
/// including the in-memory paging: no single query knows a row's position in an ordered union.</para>
///
/// <para>⚠️ <b>The bridged-plan de-duplication applies here too</b>, and it is the reason this is not simply two
/// independent lists. <c>IssueInvoiceCommand</c> carries a bridged plan's cheque across onto the invoice payment,
/// so counting the plan side as well would list one physical cheque twice — and the duplicate would be
/// indistinguishable from a second genuine cheque of the same amount from the same bank.</para>
///
/// <para>⚠️ <b>Outstanding by default</b> (Group B). A cheque marked as taken to the bank leaves this list unless
/// <see cref="Banked"/> asks for the other side; the ordering is by due date and the response leads with
/// per-bucket counts because « en retard » is the actionable set. What the screen must never do is silently drop
/// old rows to look tidy — that would discard precisely the forgotten cheque it exists to surface, which is why a
/// cheque leaves only by being banked <i>on purpose</i> or by being voided.</para>
/// </summary>
public class GetChequesDueQuery : IRequest<Result<ChequesDueDto>>
{
    /// <summary>
    /// Inclusive lower / upper bound on the cheque's <b>due date</b> — not on when it was received. Omit both for
    /// every cheque held (the default, which the soonest-due ordering already makes useful).
    /// <para>
    /// ⚠️ A cheque with <b>no</b> due date is returned whatever the bounds say. It cannot satisfy a date filter and
    /// it is the one row most likely to be forgotten, so it is always present and always counted in its own group.
    /// </para>
    /// </summary>
    public DateTime? DueFrom { get; set; }

    /// <inheritdoc cref="DueFrom"/>
    public DateTime? DueTo { get; set; }

    /// <summary>1-based page and page size. Both null = every cheque held.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>Free-text filter over the cheque number, the bank, the patient and the document reference.</summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Which side of the life-cycle to list: <c>false</c> (and <b>null</b>, the default) = still held,
    /// <c>true</c> = already taken to the bank.
    ///
    /// <para>Outstanding is the default because this screen is a to-do list: it is opened to answer « what do I
    /// still have to bank? », and a list that led with cheques already deposited would bury that answer. The
    /// banked side stays one click away rather than being unreachable — « ai-je déjà porté celui-ci ? » is the
    /// second question, and it is the one a paper drawer could never answer.</para>
    /// </summary>
    public bool? Banked { get; set; }
}

public class GetChequesDueQueryHandler : IRequestHandler<GetChequesDueQuery, Result<ChequesDueDto>>
{
    /// <summary>
    /// How far ahead « bientôt » reaches, in clinic-local days. A month is the horizon a practice plans a bank run
    /// over, and it is deliberately a constant rather than a setting: it partitions a display, it decides nothing,
    /// and every new setting in this product has to ship with a caller (the <c>SetStockExpiryLeadDays</c> lesson).
    /// </summary>
    private const int DueSoonWindowDays = 30;

    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetChequesDueQueryHandler> _logger;

    public GetChequesDueQueryHandler(
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetChequesDueQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<ChequesDueDto>> Handle(GetChequesDueQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
                return Result<ChequesDueDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            var clinicId = clinicResult.Value;

            if (request.DueFrom.HasValue && request.DueTo.HasValue && request.DueTo < request.DueFrom)
                return Result<ChequesDueDto>.Failure("La date de fin doit être postérieure à la date de début.");

            // The same de-duplication every money read applies. See the type remarks: here it is not a consistency
            // nicety but the difference between one cheque and two.
            var billedPlanIds = PlanBillingRules.BilledPlanIds(
                await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken));

            // Sequential: the two reads share the request's DbContext, so Task.WhenAll throws. Same constraint the
            // dashboard readers and the caisse statement document.
            var invoiceCheques = await _invoiceRepository.GetChequePaymentsAsync(
                clinicId, request.DueFrom, request.DueTo, cancellationToken);
            var planCheques = await _planRepository.GetInstallmentChequePaymentsAsync(
                clinicId, billedPlanIds, request.DueFrom, request.DueTo, cancellationToken);

            // Names in one pass, exactly as the statement resolves them — neither row type carries a name, because
            // neither `Invoice` nor `TreatmentPlan` has a `Patient` navigation to project from.
            var patientIds = invoiceCheques.Select(c => c.PatientId)
                .Concat(planCheques.Select(c => c.PatientId))
                .Distinct()
                .ToList();
            var patients = await _patientRepository.GetByIdsAsync(clinicId, patientIds, cancellationToken);

            // « En retard » is measured against the clinic's own today, not the server's: a cheque due today is
            // presentable today, and a UTC comparison would call it overdue for the first hour of every Tunisian day.
            var today = ClinicClock.ClinicToday();
            var dueSoonCutoff = today.AddDays(DueSoonWindowDays);

            var cheques = new List<ChequeDto>();
            cheques.AddRange(invoiceCheques.Select(c => FromInvoicePayment(c, patients, today, dueSoonCutoff)));
            cheques.AddRange(planCheques.Select(c => FromInstallmentPayment(c, patients, today, dueSoonCutoff)));

            // Soonest due first, and **undated last** — they satisfy no date question, so putting them at the front
            // would push the genuinely overdue cheques off page 1, which is the opposite of the screen's purpose.
            // They are not hidden: their count and total sit in the header, in their own group.
            // `Id` breaks the final tie, per the paging rule that every ordered read must end on a unique column —
            // otherwise OFFSET can show one cheque on two pages and skip another.
            var ordered = cheques
                .OrderBy(c => c.DueDate.HasValue ? 0 : 1)
                .ThenBy(c => c.DueDate ?? DateTime.MaxValue)
                .ThenBy(c => c.ReceivedOn)
                .ThenBy(c => c.Id)
                .ToList();

            // Groups are computed over the whole matching set, BEFORE the search filter and the page. The header
            // answers « how much am I holding », which is a fact about the clinic and not about the current page.
            //
            // ⚠️ And over OUTSTANDING cheques only, whichever side the caller asked for (AC-11): the four figures
            // answer « combien me reste-t-il à encaisser ? », so a cheque already at the bank is not part of that
            // number — and the header must not change meaning depending on which filter happens to be selected.
            var groups = BuildGroups(ordered.Where(c => !c.Banked).ToList());

            // Outstanding unless « Encaissés » is asked for explicitly. Null and false are the same request, so a
            // client that omits the parameter gets the to-do list rather than everything.
            var side = request.Banked ?? false;

            var visible = ordered
                .Where(c => c.Banked == side)
                .Where(c => string.IsNullOrWhiteSpace(request.SearchTerm)
                            || SearchTerm.Matches(
                                request.SearchTerm, c.ChequeNumber, c.BankName, c.PatientName, c.Reference))
                .ToList();

            var page = PagedResult<ChequeDto>.FromSource(visible, PageRequest.From(request.Page, request.PageSize));

            return Result<ChequesDueDto>.Success(new ChequesDueDto
            {
                Items = page.Items.ToList(),
                Groups = groups,
                Page = page.Page,
                PageSize = page.PageSize,
                TotalCount = page.TotalCount,
                TotalPages = page.TotalPages
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error building the cheques-due list");
            return Result<ChequesDueDto>.Failure("Erreur lors du calcul des chèques à encaisser.");
        }
    }

    private static string? PatientName(IReadOnlyDictionary<Guid, Patient> patients, Guid patientId) =>
        patients.TryGetValue(patientId, out var patient) ? patient.GetFullName() : null;

    private static ChequeDto FromInvoicePayment(
        CaissePaymentRow row,
        IReadOnlyDictionary<Guid, Patient> patients,
        DateTime today,
        DateTime dueSoonCutoff) => new()
        {
            Id = row.PaymentId,
            Kind = nameof(ChequeSourceKind.InvoicePayment),
            Bucket = BucketFor(row.ChequeDueDate, today, dueSoonCutoff),
            Amount = InvoiceCalculator.RoundMoney(row.Amount),
            ReceivedOn = row.PaidOn,
            DueDate = row.ChequeDueDate,
            ChequeNumber = row.ChequeNumber,
            BankName = row.ChequeBankName,
            Reference = row.InvoiceNumber,
            PatientId = row.PatientId,
            PatientName = PatientName(patients, row.PatientId),
            TargetId = row.InvoiceId,
            Banked = row.ChequeBankedOn.HasValue,
            BankedOn = row.ChequeBankedOn,
            BankedByName = row.ChequeBankedByName
        };

    private static ChequeDto FromInstallmentPayment(
        CaisseInstallmentPaymentRow row,
        IReadOnlyDictionary<Guid, Patient> patients,
        DateTime today,
        DateTime dueSoonCutoff) => new()
        {
            Id = row.PaymentId,
            Kind = nameof(ChequeSourceKind.InstallmentPayment),
            Bucket = BucketFor(row.ChequeDueDate, today, dueSoonCutoff),
            Amount = InvoiceCalculator.RoundMoney(row.Amount),
            ReceivedOn = row.PaidOn,
            DueDate = row.ChequeDueDate,
            ChequeNumber = row.ChequeNumber,
            BankName = row.ChequeBankName,
            Reference = row.PlanNumber,
            PatientId = row.PatientId,
            PatientName = PatientName(patients, row.PatientId),
            TargetId = row.TreatmentPlanId,
            InstallmentId = row.InstallmentId,
            Banked = row.ChequeBankedOn.HasValue,
            BankedOn = row.ChequeBankedOn,
            BankedByName = row.ChequeBankedByName
        };

    /// <summary>
    /// Which bucket a cheque is in. The comparison is on the <b>date</b> part: <c>ChequeDueDate</c> is a calendar
    /// day (stored with no zone conversion, like an échéance's), so comparing it as an instant would make a cheque
    /// due today read as overdue.
    /// </summary>
    private static string BucketFor(DateTime? dueDate, DateTime today, DateTime dueSoonCutoff)
    {
        if (dueDate is not { } due)
            return nameof(ChequeBucket.Undated);

        var day = due.Date;
        if (day < today) return nameof(ChequeBucket.Overdue);
        return day <= dueSoonCutoff ? nameof(ChequeBucket.DueSoon) : nameof(ChequeBucket.Later);
    }

    /// <summary>
    /// Counts and totals per bucket, derived from each row's own <see cref="ChequeDto.Bucket"/> rather than from a
    /// second date comparison — so a row can never be listed in one bucket and counted in another.
    /// </summary>
    private static ChequeGroupsDto BuildGroups(IReadOnlyList<ChequeDto> all) => new()
    {
        Overdue = Bucket(all, ChequeBucket.Overdue),
        DueSoon = Bucket(all, ChequeBucket.DueSoon),
        Later = Bucket(all, ChequeBucket.Later),
        Undated = Bucket(all, ChequeBucket.Undated),
        Total = new ChequeBucketDto
        {
            Count = all.Count,
            Amount = InvoiceCalculator.RoundMoney(all.Sum(c => c.Amount))
        }
    };

    private static ChequeBucketDto Bucket(IReadOnlyList<ChequeDto> all, ChequeBucket bucket)
    {
        var name = bucket.ToString();
        var rows = all.Where(c => c.Bucket == name).ToList();
        return new ChequeBucketDto
        {
            Count = rows.Count,
            Amount = InvoiceCalculator.RoundMoney(rows.Sum(c => c.Amount))
        };
    }
}

/// <summary>
/// Which ledger a cheque came from. Its two names deliberately match <c>CaisseMovementKind</c>'s, so a client
/// reading both screens does not learn two vocabularies for one distinction.
/// </summary>
public enum ChequeSourceKind
{
    InvoicePayment,
    InstallmentPayment
}

/// <summary>The bucket names, declared once so the row's <c>Bucket</c> and the header's groups cannot use different spellings.</summary>
public enum ChequeBucket
{
    Overdue,
    DueSoon,
    Later,
    Undated
}
