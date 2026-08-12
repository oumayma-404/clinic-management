using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Messaging;

/// <summary>
/// <b>May this cabinet's queued WhatsApp reminders leave the building?</b> FR-4's and FR-7's enforcement, consulted at
/// dispatch <i>and</i> at un-park (AC-4.8): a cabinet that has spent its forfait has its reminders <b>held</b> rather
/// than sent or failed, and they go out by themselves the moment the vendor grants more (AC-4.2, EC-2).
///
/// <para><b>⚠️ One gate over ORDERED TERMS, not two gates.</b> <see cref="ReviewAsync"/> returns the <i>first</i>
/// applicable of <b>template-not-ready → allowance-missing → allowance-exhausted</b>. One per-cabinet cache then covers
/// both reads (the settings row and the month row) and all four call sites have one thing to remember —
/// <see cref="OutboxSubscriptionGate"/>'s own argument against a condition written twice per queue. <b>The order is the
/// wording</b>: a cabinet with no usable template is told <i>that</i>, not that its forfait ran out, because only one
/// of the two is something anybody can act on.</para>
///
/// <para><b>⚠️ WhatsApp rows only (AC-4.6).</b> An SMS reminder for the same appointment is untouched — it is not paid
/// for out of this forfait — so a row of any other channel is never even looked up.</para>
///
/// <para><b>⚠️ Asked AFTER the subscription gate (AC-4.7, EC-8).</b> A cabinet that may not record new work at all is
/// told <i>that</i>; « forfait épuisé » would send a practice with a lapsed subscription to ask us for more messages.
/// The ordering lives in <c>NotificationJob</c>, which consults the two gates in that sequence.</para>
///
/// <para><b>One instance per tick</b>, holding today and a per-cabinet cache: a batch is oldest-first across a handful
/// of clinics, and neither the forfait nor the template state can meaningfully change mid-tick. Where the deployment
/// does not sell vendor messaging it reads <b>nothing at all</b>, so the two other deployment kinds issue not one extra
/// query (EC-16).</para>
/// </summary>
public sealed class OutboxMessagingGate
{
    private readonly IVendorMessagingAvailability _availability;
    private readonly IMessagingAllowanceRepository _allowances;
    private readonly DateTime _clinicToday;
    private readonly Dictionary<Guid, OutboxBlock?> _decided = new();

    /// <param name="clinicToday">
    /// The clinic-local day, resolved once by the caller through <c>ClinicClock.ClinicToday()</c> — the same reason
    /// <see cref="OutboxSubscriptionGate"/> takes one. It fixes which Tunisian month the whole tick is measured
    /// against, so two rows of one tick cannot be charged to different months across a rollover, and it makes the
    /// boundary testable (EC-7).
    /// </param>
    public OutboxMessagingGate(
        IVendorMessagingAvailability availability,
        IMessagingAllowanceRepository allowances,
        DateTime clinicToday)
    {
        _availability = availability;
        _allowances = allowances;
        _clinicToday = clinicToday;
    }

    /// <summary>The Tunisian month this gate is metering — the one the whole tick is charged to.</summary>
    public string MonthKey => ClinicClock.MonthKey(_clinicToday);

    /// <summary>
    /// The day the forfait renews — the first of the next Tunisian month, off the same <c>clinicToday</c> the whole
    /// tick shares. Exposed so the parked row's sentence and the 100 % warning name <b>one</b> date rather than each
    /// reading the clock again; two reads either side of Tunisian midnight would disagree.
    ///
    /// <para>⚠️ It is a fact about the <b>allowance</b>, never a promise about the held reminders (AC-4.2).</para>
    /// </summary>
    public DateTime RenewsOn => ClinicClock.FirstDayOfNextMonth(_clinicToday);

    /// <summary>
    /// Null when the row may be sent, otherwise what to park it with. Nullable rather than a two-field verdict so a
    /// caller cannot read the reason of a decision that was « send ».
    /// </summary>
    /// <param name="channel">
    /// The row's channel. Anything but <see cref="NotificationType.WhatsApp"/> returns null without a query (AC-4.6).
    /// </param>
    /// <param name="clinicId">
    /// Nullable because a reminder's is: rows enqueued before per-clinic settings existed carry none, and a row with no
    /// cabinet has no forfait to consult — the same reason the subscription gate lets such a row through.
    /// </param>
    public async Task<OutboxBlock?> ReviewAsync(
        NotificationType channel, Guid? clinicId, CancellationToken cancellationToken = default)
    {
        if (!_availability.SellsVendorMessaging
            || channel != NotificationType.WhatsApp
            || clinicId is not { } id)
        {
            return null;
        }

        if (_decided.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var decision = await DecideAsync(id, cancellationToken);
        _decided[id] = decision;
        return decision;
    }

    /// <summary>
    /// The ordered terms. Part 4 adds the <b>template</b> term at the top of this method — the slot is declared here so
    /// that part adds a <i>term</i> rather than a second gate, and so both of the job's call sites inherit it for free.
    /// </summary>
    private async Task<OutboxBlock?> DecideAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        // ── Term 1 (Part 4 § 33a): the template is not usable ────────────────────────────────────────────────
        // Nothing here yet. It cannot live in the sender: a sender runs *after* the send call, so FR-7's « consume
        // nothing » would already be lost — Meta would refuse an unapproved template and the row would burn three
        // retries, or Meta would accept and a unit would be counted against a template the cabinet cannot use.

        // ── Term 2: no allowance record at all (AC-4.3) ──────────────────────────────────────────────────────
        var month = await _allowances.GetMonthAsync(clinicId, MonthKey, cancellationToken);
        if (month is null)
        {
            // ⚠️ Deliberately NOT the same answer as the subscription gate gives a cabinet with no entitlement row,
            // which is « keep sending ». There the work was already recorded legitimately and silence would be
            // invisible; here the missing row is what the send is *metered against*, so sending would spend the
            // vendor's own credit line against a cabinet nothing is tracking. It is held under its own reason and its
            // own sentence, and never presented as « épuisé » — the practice has nothing to have spent.
            return new OutboxBlock(OutboxBlockReason.MessagingAllowanceMissing, MessagingRefusals.ParkedMissing);
        }

        // ── Term 3: the forfait is spent (AC-4.1) ────────────────────────────────────────────────────────────
        if (month.IsExhausted)
        {
            return new OutboxBlock(
                OutboxBlockReason.MessagingAllowanceExhausted,
                MessagingRefusals.ParkedExhausted(RenewsOn));
        }

        return null;
    }
}
