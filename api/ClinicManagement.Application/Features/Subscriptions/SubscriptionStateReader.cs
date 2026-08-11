using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Subscriptions;

/// <summary>Where a cabinet stands, derived. Nothing here is stored (FR-1).</summary>
/// <param name="DaysRemaining">
/// Whole clinic-local days left, <b>0 on the last working day</b> — the cabinet may work all of
/// <see cref="ClinicSubscription.EndsOn"/> (AC-1.1). Null when there is no end date, and null once the date has
/// passed: a negative countdown is never surfaced, because « −3 jours restants » is not a thing to tell anybody.
/// </param>
public sealed record SubscriptionStatus(
    SubscriptionState State,
    bool AllowsWrites,
    bool ShouldWarn,
    int? DaysRemaining,
    DateTime? EndsOn);

/// <summary>
/// <b>The one FR-1 rule</b>: entitlement + the clinic's own today → state, whether writes are allowed, whether to
/// warn, and the countdown. Read by the gate, the « Abonnement » screen, the banner, the warning job, the report
/// and every vendor verb, so none of them can answer « is this cabinet expired? » differently.
///
/// <para><b>Pure and clock-free.</b> Today arrives as a parameter through <c>ClinicClock.ClinicToday()</c>; a
/// reader that read the clock itself could not be tested across a midnight, which is the one boundary that
/// matters here.</para>
/// </summary>
public static class SubscriptionStateReader
{
    /// <summary>
    /// The thresholds a cabinet is warned on: 7, 3 and 1 day(s) before, and again on the day it ends (AC-3.4).
    /// <b>Four distinct notifications</b>, deduped per threshold — the banner appears from the first of them.
    /// </summary>
    public static readonly IReadOnlyList<int> WarningThresholds = new[] { 7, 3, 1, 0 };

    /// <summary>How many days before the end the banner starts showing (AC-3.1) — the largest threshold.</summary>
    public static int WarningWindowDays => WarningThresholds[0];

    /// <param name="isTrial">
    /// Whether the cover in force is the free trial, which only changes the <i>label</i> — it changes nothing about
    /// writes or warnings, and a trial is not a reduced product (AC-1.4).
    ///
    /// <para>A parameter rather than something read off the entitlement, because the gate must stay one indexed row:
    /// deciding Trial-vs-Active needs the ledger, and the gate does not care which of the two it is. The
    /// « Abonnement » screen reads the ledger anyway for its history, so it can say.</para>
    /// </param>
    public static SubscriptionStatus Read(
        ClinicSubscription subscription, DateTime clinicToday, bool isTrial = false)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var endsOn = subscription.EndsOn?.Date;
        var today = clinicToday.Date;

        // ⚠️ Suspension outranks everything, including an end date still in the future AND one already past
        // (EC-11: a suspended cabinet reads « Suspendu », never « Expiré »). The two have different causes and
        // different remedies, and telling a suspended practice its subscription lapsed sends it to pay again.
        if (subscription.IsSuspended)
        {
            return new SubscriptionStatus(
                SubscriptionState.Suspended,
                AllowsWrites: false,
                ShouldWarn: true,
                DaysRemaining: null,
                endsOn);
        }

        // No end date — FR-1's real « sans échéance »: every grandfathered cabinet, and every cabinet on a
        // deployment that does not enforce subscriptions. Active for ever, and never warned about.
        if (endsOn is null)
        {
            return new SubscriptionStatus(
                SubscriptionState.Active,
                AllowsWrites: true,
                ShouldWarn: false,
                DaysRemaining: null,
                EndsOn: null);
        }

        var daysRemaining = (endsOn.Value - today).Days;

        if (daysRemaining < 0)
        {
            return new SubscriptionStatus(
                SubscriptionState.Expired,
                AllowsWrites: false,
                ShouldWarn: true,
                DaysRemaining: null,
                endsOn);
        }

        return new SubscriptionStatus(
            isTrial ? SubscriptionState.Trial : SubscriptionState.Active,
            AllowsWrites: true,
            ShouldWarn: daysRemaining <= WarningWindowDays,
            daysRemaining,
            endsOn);
    }

    /// <summary>
    /// The largest threshold this countdown has reached, or null when none has — the dedupe key the warning job
    /// writes one row per (AC-3.4, AC-3.5).
    ///
    /// <para>« Largest reached » rather than « nearest », so a job that did not run for four days still produces the
    /// row for the threshold the cabinet is actually at rather than the one it slept through.</para>
    /// </summary>
    public static int? ThresholdReached(int? daysRemaining) =>
        daysRemaining is not { } days || days < 0
            ? null
            : WarningThresholds.Where(t => days <= t).Cast<int?>().LastOrDefault();

    /// <summary>
    /// <b>Why</b> a cabinet may not record new work — the one classification of a refused
    /// <see cref="SubscriptionStatus"/>, so every surface says the same thing in its own words.
    ///
    /// <para>The HTTP gate and the outbox gate each carried this three-arm switch, comments included, and only the
    /// prose differed; a third consumer would have copied the branching a third time. Callers supply their own
    /// sentences off the result — « which refusal is this? » has one answer, and only the wording is per-surface.</para>
    ///
    /// <para>⚠️ Called only for a status that already refuses. <see cref="SubscriptionRefusalKind.Inactive"/> is
    /// unreachable today — writes are refused for a suspension or for a date already past, and nothing else — but it
    /// is named rather than folded into either neighbour, which would record a reason that is not true.</para>
    /// </summary>
    public static SubscriptionRefusalKind ClassifyRefusal(SubscriptionStatus status) => status switch
    {
        // Suspension outranks a date, including one already past: a suspended cabinet is never told to renew (EC-11).
        { State: SubscriptionState.Suspended } => SubscriptionRefusalKind.Suspended,
        { EndsOn: not null } => SubscriptionRefusalKind.Expired,
        _ => SubscriptionRefusalKind.Inactive,
    };
}

/// <summary>Why writes are refused. See <see cref="SubscriptionStateReader.ClassifyRefusal"/>.</summary>
public enum SubscriptionRefusalKind
{
    /// <summary>Stopped by the vendor. Paying does not lift it, so no sentence for it may say « renouvelez ».</summary>
    Suspended,

    /// <summary>The entitlement's inclusive last day has passed. <c>SubscriptionStatus.EndsOn</c> names it.</summary>
    Expired,

    /// <summary>Refused, not suspended, and unable to say since when — our fault rather than a lapse on theirs.</summary>
    Inactive,
}
