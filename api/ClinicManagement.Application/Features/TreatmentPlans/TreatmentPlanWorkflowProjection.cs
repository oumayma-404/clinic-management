using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans;

/// <summary>
/// Derives, for a plan or a whole page of plans, which appointment currently speaks for each planned act and
/// which invoice (if any) already bills the plan — the read-back that lets a devis show where the patient
/// actually is.
/// <para>
/// Nothing here is persisted. Cancelling or deleting an appointment silently returns the act to
/// « À planifier », which is exactly why the state is derived rather than stored on
/// <see cref="TreatmentPlanItem"/> — a stored flag would need repairing, this cannot go stale.
/// </para>
/// <para>
/// Two batched reads serve every plan passed in (one appointments query, one invoice-links query), so a list
/// page never degrades into an N+1.
/// </para>
/// </summary>
public static class TreatmentPlanWorkflowProjection
{
    /// <summary>
    /// Appointment statuses that still represent a standing booking for an act. <c>Cancelled</c> and
    /// <c>NoShow</c> are deliberately absent: counting them would pin the act to « Planifié » forever *and*
    /// keep "Planifier" hidden, leaving it permanently unbookable.
    /// <para>
    /// <b>AC-P1.10 — the stated effect of the new <c>Completed → Cancelled</c> transition.</b> Cancelling a
    /// completed appointment drops it out of this set, so the act it spoke for returns to « À planifier » and
    /// becomes bookable again. That is the intended answer, not an accident: the appointment is the *only*
    /// evidence the projection has that a séance was arranged, and voiding it means there is no longer a visit
    /// to point at.
    /// </para>
    /// <para>
    /// It does <b>not</b> touch <c>TreatmentPlanItem.Status</c>. If a fiche de soins was filed, the act stays
    /// « Réalisé » on the strength of that fiche, and the correct way to undo *that* is « Détacher » (P2's
    /// un-mark), which is refused while a live invoice bills the work. So cancelling an appointment can never
    /// silently un-do clinical or financial facts — it only withdraws the booking.
    /// </para>
    /// <para>
    /// The workspace reflects this without a reload: `UpdateAppointmentCommand` lives in
    /// <c>…Features.Appointments.Commands</c>, so <c>RealtimeBroadcastBehavior</c> emits the
    /// <c>appointments</c> key, and <c>/treatment-plans/[id]</c> subscribes to it.
    /// </para>
    /// </summary>
    private static readonly HashSet<AppointmentStatus> LiveStatuses = new()
    {
        AppointmentStatus.Scheduled,
        AppointmentStatus.Confirmed,
        AppointmentStatus.InProgress,
        // A séance whose slot has passed with nobody saying what happened is still the booking that speaks for
        // this act — omitting it would revert the act to « À planifier » and offer to book a visit that exists.
        AppointmentStatus.AwaitingClosure,
        AppointmentStatus.Completed,
    };

    /// <summary>
    /// Whether an appointment in this status still represents a standing booking — the <b>single</b> answer to
    /// that question, exposed because « Traitements en cours » asks it too and works from a projection rather
    /// than from plan aggregates, so it cannot go through <see cref="BuildAsync"/>. A second copy of the set
    /// would be free to disagree about whether a no-show still books a step.
    /// </summary>
    public static bool IsLive(AppointmentStatus status) => LiveStatuses.Contains(status);

    /// <summary>
    /// The same set, as a collection a <b>SQL</b> filter can carry.
    ///
    /// <para>
    /// ⚠️ <see cref="IsLive"/> is a method call and EF cannot translate one, so « Traitements en cours » —
    /// which has to know in the database whether an act already has a séance, in order to <i>order</i> by it —
    /// could not ask through it. Exposed rather than re-listed at the call site for the reason the method's own
    /// note gives: a second copy of this set would be free to disagree about whether a visit awaiting closure
    /// still books a step, and the disagreement would be silent — the row would simply sort into the wrong
    /// group.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyCollection<AppointmentStatus> LiveAppointmentStatuses =
        LiveStatuses.ToArray();

    /// <summary>Build the derived lookups for the given plans (already tenant-checked by the caller).</summary>
    public static async Task<TreatmentPlanWorkflow> BuildAsync(
        IReadOnlyCollection<TreatmentPlan> plans,
        Guid clinicId,
        IAppointmentRepository appointmentRepository,
        IInvoiceRepository invoiceRepository,
        DateTime asOfUtc,
        CancellationToken cancellationToken,
        // Optional: the read paths supply it, the amend response does not — the frontend reloads after every
        // mutation, so a command response leaving `TreatedToothNumbers` empty costs nothing.
        IDentalRecordRepository? dentalRecordRepository = null)
    {
        var itemIds = plans.SelectMany(p => p.Items).Select(i => i.Id).ToList();

        /*
         * The fiches behind each act — read by the treated-teeth block below AND by the carried-note resolution
         * further down, which is why it is hoisted out of the former: that block is skipped when no record
         * repository is passed, and the carried note has to resolve on every read.
         *
         * ⚠️ **Both link columns, and the step one is the important half**: a stepped act takes its own
         * `LinkedDentalRecordId` only when its LAST step lands, so an act three séances into six is recorded on
         * the steps alone and reading the act's link would find nothing — which is exactly the case both readers
         * exist for. One batched read over the whole page, never one per act (§ 9.7).
         */
        var recordIdsByItem = plans
            .SelectMany(p => p.Items)
            .Select(i => (
                ItemId: i.Id,
                RecordIds: i.Steps
                    .Select(st => st.LinkedDentalRecordId)
                    .Append(i.LinkedDentalRecordId)
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .Distinct()
                    .ToList()))
            .Where(x => x.RecordIds.Count > 0)
            .ToList();

        var treatedTeeth = new Dictionary<Guid, IReadOnlyList<int>>();
        if (dentalRecordRepository is not null)
        {
            var allRecordIds = recordIdsByItem.SelectMany(x => x.RecordIds).Distinct().ToList();
            if (allRecordIds.Count > 0)
            {
                var rows = await dentalRecordRepository.GetTreatedTeethAsync(
                    clinicId, allRecordIds, cancellationToken);
                var teethByRecord = rows
                    .GroupBy(r => r.DentalRecordId)
                    .ToDictionary(g => g.Key, g => g.Select(r => r.ToothNumber).ToList());

                foreach (var (itemId, recordIds) in recordIdsByItem)
                {
                    var teeth = recordIds
                        .SelectMany(id => teethByRecord.TryGetValue(id, out var t) ? t : Enumerable.Empty<int>())
                        .Distinct()
                        .OrderBy(t => t)
                        .ToList();
                    if (teeth.Count > 0)
                    {
                        treatedTeeth[itemId] = teeth;
                    }
                }
            }
        }

        var appointments = await appointmentRepository.GetByTreatmentPlanItemIdsAsync(
            clinicId, itemIds, cancellationToken);
        var invoiceLinks = await invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken);

        // Flattened over **every** act the appointment carries out, not just the one its parent scalar names
        // (`LinkedTreatmentPlanItemIds`). A séance deliberately groups several devis acts — « ces deux-là ensemble »
        // — and keying on the scalar would leave the other acts of that same visit reporting « À planifier »,
        // offering to book a visit the patient is already coming to.
        var live = appointments.Where(a => LiveStatuses.Contains(a.Status)).ToList();

        var scheduledByItemId = live
            .SelectMany(a => a.LinkedTreatmentPlanItemIds.Select(itemId => (ItemId: itemId, Appointment: a)))
            .GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => PickRepresentative(g.Select(x => x.Appointment), asOfUtc));

        // Per-STEP, on the same rows and by the same rule. It is a second lookup rather than a refinement of the
        // one above because the two answer different questions and both are asked: « cet acte est-il planifié ? »
        // keys the act's état and stays true while any of its steps is booked, while « cette étape est-elle
        // planifiée ? » is what decides whether the strip offers « Planifier le scellement ». Deriving the second
        // from the first would make a bridge with one séance booked look entirely scheduled.
        var scheduledByStepId = live
            .SelectMany(a => a.LinkedTreatmentPlanItemStepIds.Select(stepId => (StepId: stepId, Appointment: a)))
            .GroupBy(x => x.StepId)
            .ToDictionary(g => g.Key, g => PickRepresentative(g.Select(x => x.Appointment), asOfUtc));

        // A cancelled bridge no longer represents the plan — the plan re-enters the balance and becomes
        // billable (and amendable) again, mirroring how the money reads exclude cancelled invoices.
        // One row per plan, naming the note the screens should quote — and carrying the **summed** money of
        // every live bridge, because a devis amended after it was billed raises a supplementary note and « ce
        // qui reste sur la note » is then the two together.
        var invoiceByPlanId = invoiceLinks
            .Where(l => l.Status != InvoiceStatus.Cancelled)
            .GroupBy(l => l.TreatmentPlanId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var chosen = g.OrderBy(l => l.Number ?? string.Empty).First();
                    return (
                        chosen.TreatmentPlanId,
                        chosen.InvoiceId,
                        chosen.Number,
                        chosen.Status,
                        TotalTtc: g.Sum(l => l.TotalTtc),
                        Outstanding: g.Sum(l => l.Outstanding));
                });

        /*
         * The notes d'honoraires that already collect an act this plan holds at 0 — `BilledOnInvoiceId`, keyed
         * per ACT because that is where the marker lives and where the act row has to state it.
         *
         * ⚠️ **A different question from `invoiceByPlanId` above, and the two must not be merged.** That one
         * answers « does a note REPRESENT this devis » (the bridge, `Invoice.TreatmentPlanId`), which makes
         * every money read stop looking at the plan. This one answers « does a note collect one of its acts
         * while the devis stays live », which is the opposite arrangement: the two documents are deliberately
         * disjoint and BOTH are read. Emitting a carried note through `LinkedInvoice*` would flip the plan to
         * « Facturé », hide « Encaisser » on its own échéancier and re-open the very hole
         * `ContinueRecordedActCommand` leaves the note unattached to avoid.
         *
         * One batched read for the whole page, bounded by the ids actually referenced.
         */
        /*
         * ⚠️ **The note is resolved from the FICHE, and the stored id is only the fallback — because a note gets
         * REPLACED and the stored id would then name a cancelled shell.** Three paths cancel a note, and only one
         * of them is `CancelInvoiceCommand`: correcting a note (`IssueInvoiceCommand.SupersedePredecessorAsync`)
         * and correcting the séance itself (`UpdateDentalRecordCommand.RetireForCorrectionAsync`) both void the
         * old note and raise a fresh one. Read off the stored id, the act would go back to a bare « 0,000 DT »
         * and the treatment's money would vanish from this screen the moment anybody corrected anything — the
         * reported defect, re-created by an ordinary correction, and invisible because the patient's own balance
         * stays right.
         *
         * Resolving by fiche is also what makes this **self-healing**: a fourth re-billing path added later is
         * covered with no change here, where a repoint written into each of today's three write sites would be
         * one forgotten call away from the same silence. Same reasoning as the appointment lookups above — the
         * state is derived precisely so it cannot go stale.
         *
         * `InvoiceLinkChoice.ByKey` is the single authority on « which note speaks for this fiche » (cancelled
         * dropped, issued beating a stray draft), shared with the agenda's badge and with the continuation
         * dialog, so three screens cannot name three different numbers for one séance.
         */
        var markedItems = plans
            .SelectMany(p => p.Items)
            .Where(i => i.BilledOnInvoiceId.HasValue)
            .ToList();

        var recordIdsByItemId = recordIdsByItem.ToDictionary(x => x.ItemId, x => x.RecordIds);

        var ficheNotes = markedItems.Count == 0
            ? new Dictionary<Guid, (Guid InvoiceId, string? Number)>()
            : InvoiceLinkChoice.ByKey(
                (await invoiceRepository.GetDentalRecordLinksAsync(clinicId, cancellationToken))
                    .Select(l => (l.DentalRecordId, l.InvoiceId, l.Number, l.Status)));

        // Which note now bills each marked act. Exactly one fiche behind the act, or the stored id stands:
        // an act evidenced by several fiches cannot say which of their notes carries its fee.
        var carriedInvoiceIdByItemId = new Dictionary<Guid, Guid>();
        foreach (var item in markedItems)
        {
            var records = recordIdsByItemId.TryGetValue(item.Id, out var ids) ? ids : new List<Guid>();
            carriedInvoiceIdByItemId[item.Id] =
                records.Count == 1 && ficheNotes.TryGetValue(records[0], out var currentNote)
                    ? currentNote.InvoiceId
                    : item.BilledOnInvoiceId!.Value;
        }

        var carriedInvoiceIds = carriedInvoiceIdByItemId.Values.Distinct().ToList();

        var carriedInvoiceById = carriedInvoiceIds.Count == 0
            ? new Dictionary<Guid, PlanCarriedInvoice>()
            : (await invoiceRepository.GetMoneyByIdsAsync(clinicId, carriedInvoiceIds, cancellationToken))
                // A cancelled note collects nothing and claims nothing, so it is dropped here rather than
                // rendered as money — the same call `invoiceByPlanId` makes one block up. The act's own 0 then
                // has no owner on screen, which is exactly what `NoteCarriedActGuard` prevents from arising.
                .Where(r => r.Status != InvoiceStatus.Cancelled)
                .ToDictionary(
                    r => r.InvoiceId,
                    r => new PlanCarriedInvoice(
                        r.InvoiceId, r.Number, r.Status, r.TotalTtc, r.AmountCollected, r.Outstanding));

        var carriedInvoiceByItemId = carriedInvoiceIdByItemId
            .Where(kv => carriedInvoiceById.ContainsKey(kv.Value))
            .ToDictionary(kv => kv.Key, kv => carriedInvoiceById[kv.Value]);

        // « Prochaine séance » per plan, evaluated against the same asOfUtc as the act states so a plan can
        // never claim an upcoming visit that its own acts report as past.
        var nextAppointmentAtByPlanId = plans.ToDictionary(
            p => p.Id,
            p => p.Items
                .Select(i => scheduledByItemId.TryGetValue(i.Id, out var appointment) ? appointment : null)
                .Where(a => a != null && a.AppointmentDateTime >= asOfUtc)
                .Select(a => (DateTime?)a!.AppointmentDateTime)
                .DefaultIfEmpty(null)
                .Min());

        return new TreatmentPlanWorkflow(
            scheduledByItemId, invoiceByPlanId, nextAppointmentAtByPlanId, scheduledByStepId, treatedTeeth,
            carriedInvoiceByItemId);
    }

    /// <summary>
    /// Which appointment speaks for an act when several are linked (a rebooked act): the earliest still-upcoming
    /// one, else the most recent past one — so a réalisé act still shows the visit it happened at, and an act
    /// whose visit has passed without a fiche can be surfaced as « À enregistrer » rather than « Planifié ».
    /// </summary>
    private static Appointment PickRepresentative(IEnumerable<Appointment> linked, DateTime asOfUtc)
    {
        var ordered = linked.OrderBy(a => a.AppointmentDateTime).ToList();
        return ordered.FirstOrDefault(a => a.AppointmentDateTime >= asOfUtc) ?? ordered[^1];
    }
}

/// <summary>
/// Request-scoped derived lookups consumed by <c>TreatmentPlanMappingExtensions.ToDto</c>.
/// <see cref="Empty"/> is the default for paths that don't derive (command responses) — the frontend reloads
/// after every mutation, so those leave the derived fields null rather than thread two repositories through
/// every command handler.
/// </summary>
public sealed record TreatmentPlanWorkflow(
    IReadOnlyDictionary<Guid, Appointment> ScheduledByItemId,
    IReadOnlyDictionary<Guid, (
        Guid TreatmentPlanId,
        Guid InvoiceId,
        string? Number,
        InvoiceStatus Status,
        decimal TotalTtc,
        decimal Outstanding)> InvoiceByPlanId,
    IReadOnlyDictionary<Guid, DateTime?> NextAppointmentAtByPlanId,
    IReadOnlyDictionary<Guid, Appointment> ScheduledByStepId,
    /// <summary>
    /// The teeth already treated on each devis act, unioned over the fiches its séances produced — see
    /// <c>TreatmentPlanItemDto.TreatedToothNumbers</c>. Empty when the caller supplied no record repository.
    /// </summary>
    IReadOnlyDictionary<Guid, IReadOnlyList<int>> TreatedTeethByItemId,
    /// <summary>
    /// The note d'honoraires that already collects each act the devis holds at 0 — see
    /// <c>TreatmentPlanItem.BilledOnInvoiceId</c>. Empty for every ordinary plan, and the whole reason the
    /// treatment's money can finally be stated as a whole instead of the devis' share of it.
    /// </summary>
    IReadOnlyDictionary<Guid, PlanCarriedInvoice> CarriedInvoiceByItemId)
{
    public static TreatmentPlanWorkflow Empty { get; } = new(
        new Dictionary<Guid, Appointment>(),
        new Dictionary<Guid, (Guid, Guid, string?, InvoiceStatus, decimal, decimal)>(),
        new Dictionary<Guid, DateTime?>(),
        new Dictionary<Guid, Appointment>(),
        new Dictionary<Guid, IReadOnlyList<int>>(),
        new Dictionary<Guid, PlanCarriedInvoice>());
}

/// <summary>
/// A note d'honoraires collecting an act that a <b>live, un-bridged</b> devis carries at 0.
/// <para>
/// Deliberately its own type rather than the anonymous tuple <c>InvoiceByPlanId</c> uses: the two are the same
/// six scalars answering opposite questions (« this note REPLACES the devis » versus « this note collects one of
/// its acts while the devis stays live »), and a shared shape is how a caller ends up passing one where the
/// other is meant. <c>AmountCollected</c> is carried as well as <c>Outstanding</c>, because the treatment's
/// « Encaissé » is what the devis' own <c>AmountPaid</c> cannot see.
/// </para>
/// </summary>
public sealed record PlanCarriedInvoice(
    Guid InvoiceId,
    string? Number,
    InvoiceStatus Status,
    decimal TotalTtc,
    decimal AmountCollected,
    decimal Outstanding);
