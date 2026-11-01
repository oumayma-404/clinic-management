using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Messaging;

/// <summary>
/// <b>May this cabinet's queued SMS and WhatsApp reminders leave the building?</b> FR-4's and FR-7's enforcement, consulted at
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
/// <para><b>⚠️ SMS and WhatsApp rows (<c>sms-rappels-tunisie-sms</c>, reversing AC-4.6).</b> Both channels are the
/// vendor's account and both are paid out of the one forfait: one SMS is one message. The <b>template</b> term stays
/// WhatsApp's alone — an SMS has no template to approve — which is why the decision is cached per (cabinet, channel):
/// a WhatsApp template under review must never hold the same cabinet's SMS rows.</para>
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
    private readonly IClinicReminderSettingsRepository _settings;
    private readonly DateTime _clinicToday;
    private readonly Dictionary<(Guid Clinic, NotificationType Channel), OutboxBlock?> _decided = new();

    /// <param name="clinicToday">
    /// The clinic-local day, resolved once by the caller through <c>ClinicClock.ClinicToday()</c> — the same reason
    /// <see cref="OutboxSubscriptionGate"/> takes one. It fixes which Tunisian month the whole tick is measured
    /// against, so two rows of one tick cannot be charged to different months across a rollover, and it makes the
    /// boundary testable (EC-7).
    /// </param>
    public OutboxMessagingGate(
        IVendorMessagingAvailability availability,
        IMessagingAllowanceRepository allowances,
        IClinicReminderSettingsRepository settings,
        DateTime clinicToday)
    {
        _availability = availability;
        _allowances = allowances;
        _settings = settings;
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
    /// The row's channel. Anything but SMS or WhatsApp returns null without a query — no other channel is sold.
    /// </param>
    /// <param name="clinicId">
    /// Nullable because a reminder's is: rows enqueued before per-clinic settings existed carry none, and a row with no
    /// cabinet has no forfait to consult — the same reason the subscription gate lets such a row through.
    /// </param>
    public async Task<OutboxBlock?> ReviewAsync(
        NotificationType channel, Guid? clinicId, CancellationToken cancellationToken = default)
    {
        if (!_availability.SellsVendorMessaging
            || !IsMetered(channel)
            || clinicId is not { } id)
        {
            return null;
        }

        if (_decided.TryGetValue((id, channel), out var cached))
        {
            return cached;
        }

        var decision = await DecideAsync(id, channel, cancellationToken);
        _decided[(id, channel)] = decision;
        return decision;
    }

    /// <summary>The channels paid out of the forfait — the vendor's own SMS account and its WhatsApp numbers.</summary>
    public static bool IsMetered(NotificationType channel) =>
        channel is NotificationType.SMS or NotificationType.WhatsApp;

    /// <summary>
    /// The ordered terms. Part 4 adds the <b>template</b> term at the top of this method — the slot is declared here so
    /// that part adds a <i>term</i> rather than a second gate, and so both of the job's call sites inherit it for free.
    /// </summary>
    private async Task<OutboxBlock?> DecideAsync(Guid clinicId, NotificationType channel, CancellationToken cancellationToken)
    {
        // ── Term 1 (§ 33a): the template is not usable (FR-7) ────────────────────────────────────────────────
        // It cannot live in the sender: a sender runs *after* the send call, so FR-7's « consume nothing » would
        // already be lost — Meta would refuse an unapproved template and the row would burn three retries, or Meta
        // would accept and a unit would be counted against a template the cabinet cannot use.
        //
        // ⚠️ A cabinet with NO stored status passes. That is « we do not track a template for this cabinet », which
        // is true of every cabinet sending today on the install's own pre-approved one — holding those would stop
        // every reminder on the deployment the day this shipped, for a template Meta approved long ago.
        var settings = channel == NotificationType.WhatsApp
            ? await _settings.GetByClinicIdAsync(clinicId, cancellationToken)
            : null;
        if (settings?.WhatsAppTemplateStatus is { } template && template != WhatsAppTemplateStatus.Approved)
        {
            return new OutboxBlock(
                OutboxBlockReason.MessagingTemplateNotReady,
                MessagingRefusals.ParkedTemplateNotReady(template));
        }

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
