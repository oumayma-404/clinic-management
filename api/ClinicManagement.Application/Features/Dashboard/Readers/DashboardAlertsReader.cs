using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Application.Features.Recall;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>
/// « À traiter » — what needs attention right now, across the subsystems that had never reached the first screen:
/// the salle d'attente, devis awaiting an answer, relances due, prostheses overdue at the lab, and stock.
///
/// <para>Every figure is a count with a matching filtered destination, and each count reuses the <i>same</i> predicate
/// its destination list uses (see the repository methods) so a card can never disagree with the page it opens.</para>
///
/// <para>The stock figures overlap the in-app notification feed. That is intentional and not redundancy: the feed is a
/// per-user, transient stream of events which a user can mark read, whereas this is the persistent answer to « what is
/// wrong right now ». Marking a low-stock notification read must not make the shortage disappear.</para>
/// </summary>
public class DashboardAlertsReader : IDashboardAlertsReader
{
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ILabWorkOrderRepository _labWorkOrderRepository;
    private readonly IStockItemRepository _stockItemRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IInvoiceRepository _invoiceRepository;

    public DashboardAlertsReader(
        IWaitingListRepository waitingListRepository,
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ILabWorkOrderRepository labWorkOrderRepository,
        IStockItemRepository stockItemRepository,
        IClinicRepository clinicRepository,
        IAppointmentRepository appointmentRepository,
        IDentalRecordRepository dentalRecordRepository,
        IInvoiceRepository invoiceRepository)
    {
        _waitingListRepository = waitingListRepository;
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _labWorkOrderRepository = labWorkOrderRepository;
        _stockItemRepository = stockItemRepository;
        _clinicRepository = clinicRepository;
        _appointmentRepository = appointmentRepository;
        _dentalRecordRepository = dentalRecordRepository;
        _invoiceRepository = invoiceRepository;
    }

    public async Task<DashboardAlertsDto> ReadAsync(Guid clinicId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);

        var waitingList = await _waitingListRepository.CountWaitingAsync(clinicId, cancellationToken);

        // Draft, not Accepted: « en attente de réponse » is a devis presented to the patient with no answer yet. An
        // accepted-but-unstarted plan is a different state (said yes, not begun) and belongs to « Devis acceptés ».
        // No date bound — a devis from three months ago with no answer is exactly the one worth chasing.
        var draftPlans = await _planRepository.CountByStatusAsync(
            clinicId, TreatmentPlanStatus.Draft, cancellationToken: cancellationToken);

        var patientsToRecall = await CountPatientsToRecallAsync(clinicId, clinic?.RecallIntervalMonths, nowUtc, cancellationToken);

        var overdueLabOrders = await _labWorkOrderRepository.CountOverdueAsync(clinicId, nowUtc, cancellationToken);

        var lowStock = await _stockItemRepository.CountLowStockAsync(clinicId, cancellationToken);

        // A non-positive lead window means the clinic switched the approaching-expiry alert off — the same reading
        // StockExpiryJob applies before it scans. Do not query, and report the alert as disabled rather than as zero:
        // « 0 » claims nothing is expiring, when the truth is that nothing was looked at.
        var leadDays = clinic?.StockExpiryLeadDays ?? 0;
        var expiryAlertEnabled = leadDays > 0;
        var expiringStock = expiryAlertEnabled
            ? await _stockItemRepository.CountExpiringSoonAsync(clinicId, leadDays, nowUtc, cancellationToken)
            : 0;

        // « À clôturer » — read through the very helper the worklist itself uses, never a second predicate, so
        // the chip and the page it opens cannot report different numbers. It is a count of rows rather than a
        // COUNT(*) because the rule is not expressible in SQL (the end-of-slot test, and the three-way gap), and
        // what bounds it is the window: a clinic's recent agenda, not its history.
        var visitsToClose = await VisitClosureReader.ReadAsync(
            clinicId,
            days: null,
            doctorId: null,
            nowUtc,
            _appointmentRepository,
            _dentalRecordRepository,
            _invoiceRepository,
            _planRepository,
            cancellationToken);

        return new DashboardAlertsDto
        {
            WaitingList = waitingList,
            // `.Open`, never the whole worklist: a séance the practice has set aside is off the list, so a chip
            // counting it would send the owner to a page that does not show it.
            VisitsToClose = visitsToClose.Open.Count,
            DraftPlans = draftPlans,
            PatientsToRecall = patientsToRecall,
            OverdueLabOrders = overdueLabOrders,
            LowStock = lowStock,
            ExpiringStock = expiringStock,
            ExpiryAlertEnabled = expiryAlertEnabled
        };
    }

    /// <summary>
    /// How many patients are due a relance — the same two-step rule « patients à relancer » uses, through the shared
    /// <see cref="RecallDueRule"/>: a widened bound in SQL, then the exact <c>AddMonths</c> test in memory.
    ///
    /// <para>This is the one alert that cannot be a pure <c>COUNT</c>, because the exact rule is not expressible in SQL
    /// (the end-of-month clamp does not survive inversion). The candidate set is already bounded by the repository, so
    /// counting what survives the exact test costs the same read the relance page itself performs — and guarantees the
    /// card and the page show the same number, which a separate approximate count would not.</para>
    /// </summary>
    private async Task<int> CountPatientsToRecallAsync(
        Guid clinicId, int? clinicIntervalMonths, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var intervalMonths = clinicIntervalMonths ?? RecallDueRule.DefaultIntervalMonths;
        var anchorOnOrBefore = RecallDueRule.AnchorUpperBound(nowUtc, intervalMonths);

        var candidates = await _patientRepository.GetRecallCandidatesAsync(
            clinicId, anchorOnOrBefore, nowUtc, cancellationToken: cancellationToken);

        return candidates.Count(c => RecallDueRule.IsDue(c.RecallAnchorUtc, intervalMonths, nowUtc));
    }
}
