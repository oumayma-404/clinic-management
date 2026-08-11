using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Maintenance;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// The pass behind every activity figure in the vendor console (<c>platform-console</c> FR-3, AC-2.4a, EC-8).
///
/// <para><b>Why a scheduled pass and not a per-request derivation.</b> The portfolio can be filtered and sorted
/// <i>on activity</i>, so those figures have to exist for every cabinet before a page is cut — a figure derived
/// over the already-selected page would filter and sort a window rather than the portfolio. Deriving them per
/// request would also scan every practice's mutation ledger on every keystroke, bounding the read by the busiest
/// cabinet's whole history instead of by the number of cabinets (EC-11).</para>
///
/// <para>⚠️ <b>Deliberately NOT connectivity-gated</b>, unlike <see cref="NotificationJob"/> and
/// <see cref="PushDispatchJob"/> and for <see cref="StockExpiryJob"/>'s reason: its output is a database row, not
/// an outbound message. Gating it on internet egress would leave the counters frozen through an outage and then
/// report the resulting silence as cabinets going dormant.</para>
///
/// <para>⚠️ <b>A cabinet with nothing to count gets rows of zeros, never no row</b> (EC-8). « Aucune activité »
/// is the churn signal the portfolio exists to give; a missing row is indistinguishable from a pass that never
/// ran, and the two have opposite meanings for the vendor.</para>
///
/// <para>⚠️ <b>It rewrites the whole 30-day window each run, not only yesterday.</b> The audit rows for that
/// window are already in hand — the snapshot needs them — so writing the day rows costs nothing extra, and it is
/// what makes the history self-healing: a container down for three days, a first deployment, a failed run all
/// fill in on the next pass instead of leaving permanent holes in a trend nothing can reconstruct afterwards.
/// The unique index on (cabinet, day) is what makes that a restatement rather than duplication.</para>
/// </summary>
public class ClinicActivityCounterJob
{
    /// <summary>The window every « 30 j » figure is measured over, in clinic-local days, ending today inclusive.</summary>
    private const int WindowDays = 30;

    /// <summary>The short window, same convention.</summary>
    private const int ShortWindowDays = 7;

    private readonly IClinicRepository _clinicRepository;
    private readonly IClinicActivityRepository _activityRepository;
    private readonly IAuditEntryRepository _auditRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUserRepository _userRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<ClinicActivityCounterJob> _logger;

    public ClinicActivityCounterJob(
        IClinicRepository clinicRepository,
        IClinicActivityRepository activityRepository,
        IAuditEntryRepository auditRepository,
        IPatientRepository patientRepository,
        IUserRepository userRepository,
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository planRepository,
        ICreditNoteRepository creditNoteRepository,
        IUnitOfWork unitOfWork,
        IAuditActorProvider auditActor,
        ITenantScope tenantScope,
        ILogger<ClinicActivityCounterJob> logger)
    {
        _clinicRepository = clinicRepository;
        _activityRepository = activityRepository;
        _auditRepository = auditRepository;
        _patientRepository = patientRepository;
        _userRepository = userRepository;
        _invoiceRepository = invoiceRepository;
        _planRepository = planRepository;
        _creditNoteRepository = creditNoteRepository;
        _unitOfWork = unitOfWork;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 3)]
    public async Task CountClinicActivity()
    {
        _auditActor.RunAs(nameof(ClinicActivityCounterJob));

        // US-2: this reads Patients, Invoices and TreatmentPlans — all clinic-filtered — for every cabinet.
        // Without the declaration it would find nothing anywhere and log a clean pass, which is R-1's exact shape
        // and would surface as a portfolio where every practice looks idle.
        _tenantScope.UseSystemWide("ClinicActivityCounterJob measures every cabinet's activity for the vendor console");

        var nowUtc = DateTime.UtcNow;
        var clinics = await _clinicRepository.GetAllAsync();

        var snapshots = (await _activityRepository.GetAllSnapshotsAsync())
            .ToDictionary(s => s.ClinicId);

        foreach (var clinic in clinics)
        {
            try
            {
                await CountClinicAsync(clinic, snapshots.GetValueOrDefault(clinic.Id), nowUtc);
            }
            catch (Exception ex)
            {
                // One cabinet's failure must not cost the other ninety-nine their measurement — and a cabinet
                // skipped keeps its previous snapshot, whose ComputedAt then honestly reports it as stale.
                _logger.LogError(ex, "Activity counting failed for clinic {ClinicId}", clinic.Id);
            }
        }
    }

    private async Task CountClinicAsync(Clinic clinic, ClinicActivitySnapshot? existing, DateTime nowUtc)
    {
        var todayLocal = ClinicClock.ClinicToday(nowUtc);
        var windowFrom = ClinicClock.StartOfLocalDayUtc(todayLocal.AddDays(-(WindowDays - 1)));
        // The LAST TICK of today, not the next midnight: EndOfLocalDayUtc is exclusive while every windowed read
        // in this codebase is inclusive on both ends, and the exclusive bound counts a midnight save twice.
        var windowTo = ClinicClock.LastTickOfLocalDayUtc(todayLocal);

        var rows = await _auditRepository.GetActivityRowsAsync(clinic.Id, windowFrom, windowTo);

        await WriteDayRowsAsync(clinic.Id, rows, todayLocal, nowUtc);

        var window30 = PlatformCounterPass.Count(rows, windowFrom, windowTo);
        var window7 = PlatformCounterPass.Count(
            rows, ClinicClock.StartOfLocalDayUtc(todayLocal.AddDays(-(ShortWindowDays - 1))), windowTo);

        var patients = await _patientRepository.CountByClinicIdAsync(clinic.Id);
        var staff = await _userRepository.GetStaffSummaryAsync(clinic.Id);
        var collected = await CollectedThisMonthAsync(clinic.Id, todayLocal);

        var snapshot = existing ?? new ClinicActivitySnapshot(clinic.Id);
        snapshot.Restate(
            writes7d: window7.Writes,
            writes30d: window30.Writes,
            appointments30d: window30.Appointments,
            activeDays30d: window30.ActiveDays,
            lastWriteAt: window30.LastWriteAt,
            patients: patients,
            users: staff.Count,
            lastLoginAt: staff.LastLoginAt,
            collectedThisMonth: collected,
            computedAt: nowUtc);

        if (existing is null)
        {
            await _activityRepository.AddSnapshotAsync(snapshot);
        }

        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// Restates every day of the window, so a cabinet that did nothing on a day still has that day's row of
    /// zeros (EC-8) and a run that was missed fills itself in.
    /// </summary>
    private async Task WriteDayRowsAsync(
        Guid clinicId, IReadOnlyList<ClinicActivityAuditRow> rows, DateTime todayLocal, DateTime nowUtc)
    {
        for (var offset = WindowDays - 1; offset >= 0; offset--)
        {
            var localDate = todayLocal.AddDays(-offset);
            var day = DateOnly.FromDateTime(localDate);
            var counts = PlatformCounterPass.Count(
                rows, ClinicClock.StartOfLocalDayUtc(localDate), ClinicClock.LastTickOfLocalDayUtc(localDate));

            var existing = await _activityRepository.GetDayAsync(clinicId, day);
            if (existing is null)
            {
                await _activityRepository.AddDayAsync(new ClinicActivityDay(
                    clinicId, day, counts.Writes, counts.Appointments, counts.PatientsCreated, nowUtc));
            }
            else
            {
                existing.Restate(counts.Writes, counts.Appointments, counts.PatientsCreated, nowUtc);
            }
        }
    }

    /// <summary>
    /// What the cabinet itself collected in the current clinic-local month, month-to-date — through
    /// <see cref="PlatformCollectedReader"/>, which is where the « fifth money read » argument lives and which is
    /// what <c>MoneyReadConsistencyTests</c> holds equal to la caisse.
    ///
    /// <para>Month-to-date rather than the whole month: the figure is stated as of <c>ComputedAt</c>, and a
    /// window running to the end of a month not yet over would be a bound nothing can be measured against.</para>
    /// </summary>
    private async Task<decimal> CollectedThisMonthAsync(Guid clinicId, DateTime todayLocal)
    {
        var monthFrom = ClinicClock.StartOfLocalDayUtc(new DateTime(todayLocal.Year, todayLocal.Month, 1));
        var monthTo = ClinicClock.LastTickOfLocalDayUtc(todayLocal);

        return await PlatformCollectedReader.ReadAsync(
            _invoiceRepository, _planRepository, _creditNoteRepository, clinicId, monthFrom, monthTo);
    }
}
