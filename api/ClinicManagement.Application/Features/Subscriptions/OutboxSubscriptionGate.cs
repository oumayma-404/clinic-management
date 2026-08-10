using System.Globalization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Subscriptions;

/// <summary>
/// Why a cabinet's queued outbound work is parked, machine-readably and in French — the pair a dispatcher writes onto
/// the row and the reviewer reads back off it.
/// </summary>
public sealed record OutboxBlock(OutboxBlockReason Reason, string Sentence);

/// <summary>
/// <b>May this cabinet's queued sends leave the building?</b> One answer for the reminder outbox and the OS-push
/// outbox, at dispatch <i>and</i> at un-park (<c>clinic-subscription</c> FR-8, EC-7): SMS, WhatsApp and push all stop
/// while a cabinet may not record new work, and a queued row is <b>parked</b> rather than sent or discarded, so
/// extending the entitlement before the visit still gets the reminder out.
///
/// <para><b>⚠️ The un-park term is the half that must not be forgotten.</b> Both reviewers ask only whether the
/// <i>channel</i> can send — is there a sender, is it enabled for this clinic, are its credentials present — and a
/// row parked for expiry passes all three, so it would be released and dispatched within a minute on a cabinet that
/// has not paid. Hence one gate consulted from four places rather than a condition written twice per queue.</para>
///
/// <para><b>⚠️ A cabinet with no entitlement row keeps sending.</b> Unlike the HTTP gate — where fail-closed is right,
/// because a missing row must not become a way to write for ever — nothing here is an authorization decision: the
/// work was already recorded, legitimately, while the cabinet could write. <see cref="OutboxBlockReason"/> has no
/// member for it either, and « parked because our own bookkeeping is broken » would silence a practice's reminders
/// over a fault it cannot see. That fault is surfaced where it can be acted on: <c>verify-schema</c>'s
/// <c>every-clinic-has-an-entitlement</c> and the <c>subscription-report</c> verb (FR-13).</para>
///
/// <para><b>One instance per tick</b>, holding today and a per-cabinet cache: a batch is oldest-first across a
/// handful of clinics, and the entitlement cannot meaningfully change mid-tick. Where subscriptions are not enforced
/// it reads nothing at all, so the two other deployment kinds issue not one extra query (AC-7.1/7.2).</para>
/// </summary>
public sealed class OutboxSubscriptionGate
{
    private readonly ISubscriptionPolicy _policy;
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly DateTime _clinicToday;
    private readonly Dictionary<Guid, OutboxBlock?> _decided = new();

    /// <param name="clinicToday">
    /// The clinic-local day, resolved once by the caller through <c>ClinicClock.ClinicToday()</c> — the same reason
    /// <see cref="SubscriptionStateReader"/> and <c>SubscriptionWarningJob</c> take one: midnight is the only boundary
    /// that matters for a date that arrives by itself, and a gate reading the clock itself could not be tested across
    /// it. It also keeps two rows of one tick from being measured against different days.
    /// </param>
    public OutboxSubscriptionGate(
        ISubscriptionPolicy policy, IClinicSubscriptionRepository subscriptions, DateTime clinicToday)
    {
        _policy = policy;
        _subscriptions = subscriptions;
        _clinicToday = clinicToday;
    }

    /// <summary>
    /// Null when the row may be sent, otherwise what to park it with. Nullable rather than a two-field verdict so a
    /// caller cannot read the reason of a decision that was « send ».
    /// </summary>
    /// <param name="clinicId">
    /// Nullable because a reminder's is: rows enqueued before per-clinic settings existed carry none, and a row with
    /// no cabinet has no entitlement to consult — the same reason the HTTP gate lets a caller who is not a cabinet
    /// through.
    /// </param>
    public async Task<OutboxBlock?> ReviewAsync(Guid? clinicId, CancellationToken cancellationToken = default)
    {
        if (!_policy.RequiresSubscription || clinicId is not { } id)
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

    private async Task<OutboxBlock?> DecideAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptions.GetByClinicAsync(clinicId, cancellationToken);
        if (subscription is null)
        {
            // See the ⚠️ on the class: a missing row is our fault, not a lapse, and it is reported elsewhere.
            return null;
        }

        var status = SubscriptionStateReader.Read(subscription, _clinicToday);
        if (status.AllowsWrites)
        {
            return null;
        }

        var sentence = status switch
        {
            // Suspension outranks a date, as everywhere else: a suspended cabinet is never told to renew (EC-11).
            { State: SubscriptionState.Suspended } => Suspended,
            { EndsOn: { } endsOn } => Expired(endsOn),
            // Unreachable — writes are refused only for a suspension or a date already past — but stated rather
            // than folded into one of the two above, which would record a sentence that is not true.
            _ => Inactive,
        };

        return new OutboxBlock(OutboxBlockReason.SubscriptionExpired, sentence);
    }

    /// <summary>
    /// The wording is channel-neutral — one sentence for a parked SMS, a parked WhatsApp message and a parked push —
    /// and says the send is <b>waiting</b> rather than failed, because that is what parking means: nothing was lost
    /// and nothing was attempted.
    /// </summary>
    public static string Expired(DateTime endsOn) =>
        $"Abonnement du cabinet expiré le {endsOn.ToString(SubscriptionRefusals.DateFormat, CultureInfo.InvariantCulture)}"
        + " — envoi en attente du renouvellement";

    public const string Suspended = "Accès du cabinet suspendu — envoi en attente du rétablissement";

    public const string Inactive = "Abonnement du cabinet inactif — envoi en attente";
}
