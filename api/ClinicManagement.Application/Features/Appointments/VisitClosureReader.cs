using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments;

/// <summary>
/// One open séance and everything a row needs about it.
/// </summary>
public sealed record OpenVisit(
    Appointment Appointment,
    VisitClosureState State,
    Guid? DentalRecordId,
    AppointmentInvoiceLinks.Link? Invoice);

/// <summary>
/// Assembles « à clôturer » — the four batched reads, the exact end-of-slot test the database cannot do, and
/// <see cref="VisitClosureRules"/> applied to the result.
///
/// <para><b>Why it is a shared helper and not a private method on the query.</b> Two callers need this: the
/// worklist itself and the dashboard's « À clôturer » chip. A count derived separately from the list it links to
/// is the defect <c>DashboardAlertsReader</c> already legislates against for every other figure it carries —
/// « each count reuses the <i>same</i> predicate its destination list uses, so a card can never disagree with the
/// page it opens ». Two copies of a rule this shaped would disagree the first time either side gained a term,
/// and both screens would look right on their own.</para>
///
/// <para><b>Static, taking its repositories as parameters</b>, on <c>AppointmentInvoiceLinks</c>' and
/// <c>WorkingHoursResolver</c>'s precedent: it holds no state, and leaving it out of the container keeps the
/// dependency list of each caller honest about what it actually reads.</para>
/// </summary>
public static class VisitClosureReader
{
    /// <summary>Default window, in clinic-local days including today.</summary>
    public const int DefaultDays = 7;
    public const int MinDays = 1;
    public const int MaxDays = 90;

    /// <summary>
    /// Clamped, never refused — a stale bookmark asking for « ?days=0 » should show rows, not a French error.
    /// <c>PageRequest</c>'s reasoning, one parameter over.
    /// </summary>
    public static int ResolveDays(int? days) => Math.Clamp(days ?? DefaultDays, MinDays, MaxDays);

    /// <summary>
    /// The clinic's still-open séances over the window, most recent first.
    /// </summary>
    /// <param name="days">Clinic-local days back, including today. See <see cref="ResolveDays"/>.</param>
    /// <param name="doctorId">Optional practitioner filter.</param>
    /// <param name="nowUtc">Taken from the caller so the boundary is testable — the pattern
    /// <c>SubscriptionWarningJob</c> and <c>AppointmentProgressJob</c> both follow.</param>
    public static async Task<IReadOnlyList<OpenVisit>> ReadAsync(
        Guid clinicId,
        int? days,
        Guid? doctorId,
        DateTime nowUtc,
        IAppointmentRepository appointments,
        IDentalRecordRepository dentalRecords,
        IInvoiceRepository invoices,
        ITreatmentPlanRepository plans,
        CancellationToken cancellationToken = default)
    {
        // The window is the clinic's own days, never the server machine's: Tunisia is UTC+1, so « les 7 derniers
        // jours » computed from a UTC midnight starts an hour into the wrong day.
        var clinicToday = ClinicClock.ClinicToday(nowUtc);
        var fromUtc = ClinicClock.StartOfLocalDayUtc(clinicToday.AddDays(-(ResolveDays(days) - 1)));

        var candidates = await appointments.GetClosureCandidatesAsync(
            clinicId, fromUtc, nowUtc, doctorId, cancellationToken);

        if (candidates.Count == 0)
        {
            return Array.Empty<OpenVisit>();
        }

        var appointmentIds = candidates.Select(a => a.Id).ToList();

        // Three batched link reads, each bounded by this window's ids rather than clinic-wide — the rule
        // IInvoiceRepository.GetAppointmentLinksAsync states for itself, and the reason its two siblings differ.
        var ficheRows = await dentalRecords.GetAppointmentLinksAsync(clinicId, appointmentIds, cancellationToken);

        var invoiceLinks = await AppointmentInvoiceLinks.ResolveAsync(
            invoices, clinicId, appointmentIds, cancellationToken);

        // ⚠️ A visit is also billed when a live note names one of its FICHES, not only when it names the visit.
        // Invoice.AppointmentId is copied from the fiche's own link, which was null on every fiche not created
        // through the post-visit deep link — so every séance billed before that link existed carries a real,
        // paid note this read could not see, and the worklist asked the practice to collect the money twice.
        var billedFicheIds = (await invoices.GetDentalRecordLinksAsync(clinicId, cancellationToken))
            .Where(l => l.Status != InvoiceStatus.Cancelled)
            .Select(l => l.DentalRecordId)
            .ToHashSet();

        var planItemIds = candidates
            .SelectMany(a => a.LinkedTreatmentPlanItemIds)
            .Distinct()
            .ToList();

        // A visit keeps its plan link after the devis is cancelled, so the link alone is not cover.
        var debtBearingItemIds = planItemIds.Count == 0
            ? new HashSet<Guid>()
            : (await plans.GetDebtBearingItemIdsAsync(clinicId, planItemIds, cancellationToken)).ToHashSet();

        var fichesByAppointment = ficheRows
            .GroupBy(r => r.AppointmentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var open = new List<OpenVisit>();

        foreach (var appointment in candidates)
        {
            var input = BuildInput(
                appointment, fichesByAppointment, invoiceLinks, debtBearingItemIds, billedFicheIds);

            // The end-of-slot test runs here and not in SQL: `Duration` is persisted as ticks behind a value
            // converter, so `AppointmentDateTime + Duration` has no translation, and the trigger-maintained
            // AppointmentEndDateTime column is deliberately unmapped. What makes that affordable is the window
            // above — this loop walks a clinic's recent agenda, not its history.
            if (!VisitClosureRules.IsClosable(input, nowUtc))
            {
                continue;
            }

            var state = VisitClosureRules.Evaluate(input);
            if (!state.IsOpen)
            {
                continue;
            }

            fichesByAppointment.TryGetValue(appointment.Id, out var fiches);
            invoiceLinks.TryGetValue(appointment.Id, out var invoice);

            open.Add(new OpenVisit(appointment, state, fiches?.FirstOrDefault().DentalRecordId, invoice));
        }

        // Most recent first: the client cuts this into « Aujourd'hui / Hier / mercredi 12 août », and a list
        // opening on a day three months back reads as the wrong list. The séance open longest still matters most,
        // so the day header states its age rather than the sort implying it. ⚠️ Decided HERE, never in the
        // browser: the read is paged, so reversing a page reverses only within it. The descending tie-break is
        // the unique one every paged read needs — OFFSET over a non-unique sort can show a row on two pages and
        // skip another, which on this screen reads as « une séance a disparu ».
        return open
            .OrderByDescending(o => o.Appointment.AppointmentDateTime)
            .ThenByDescending(o => o.Appointment.Id)
            .ToList();
    }

    private static VisitClosureInput BuildInput(
        Appointment appointment,
        IReadOnlyDictionary<Guid, List<(Guid AppointmentId, Guid DentalRecordId, decimal Cost)>> fiches,
        IReadOnlyDictionary<Guid, AppointmentInvoiceLinks.Link> invoiceLinks,
        IReadOnlySet<Guid> debtBearingItemIds,
        IReadOnlySet<Guid> billedFicheIds)
    {
        var hasFiche = fiches.TryGetValue(appointment.Id, out var rows) && rows.Count > 0;

        // The séance's total across every fiche recorded for it. Summed rather than taking the first, because a
        // visit may legitimately produce more than one and « rien à facturer » must mean the whole séance was
        // worth nothing — not that one of its fiches was.
        decimal? ficheCost = hasFiche ? rows!.Sum(r => r.Cost) : null;

        return new VisitClosureInput(
            AppointmentId: appointment.Id,
            PatientId: appointment.PatientId,
            Status: appointment.Status,
            StartUtc: appointment.AppointmentDateTime,
            Duration: appointment.Duration,
            HasFiche: hasFiche,
            FicheCost: ficheCost,
            HasLiveInvoice: invoiceLinks.ContainsKey(appointment.Id)
                || (hasFiche && rows!.Any(r => billedFicheIds.Contains(r.DentalRecordId))),
            CoveredByPlan: appointment.LinkedTreatmentPlanItemIds.Any(debtBearingItemIds.Contains),
            NothingToBill: appointment.IsNothingToBill);
    }
}
