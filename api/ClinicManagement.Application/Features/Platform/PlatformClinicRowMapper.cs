using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Platform;

/// <summary>
/// The one place a JOINed <see cref="PlatformClinicRow"/> becomes the row both console screens render.
///
/// <para><b>Shared rather than written twice.</b> AC-3.1 is « the same figures », and the list and the detail held
/// byte-identical mappers until Part 4 gave them something to disagree about: the entitlement state, which is
/// <i>derived</i> rather than copied. Two derivations would drift into a cabinet reading « Actif » in the portfolio
/// and « Expiré » when opened, and both screens would look right on their own.</para>
///
/// <para>⚠️ <b>The state comes from <c>SubscriptionStateReader</c> and from nothing else</b> (FR-4). The console
/// introduces no second answer to « where does this cabinet stand? » — the gate, the cabinet's own « Abonnement »
/// screen, the banner, the warning job and the vendor verbs all read that one rule, and a console-side fold would be
/// exactly the duplication this feature is defined around.</para>
/// </summary>
public static class PlatformClinicRowMapper
{
    /// <summary>
    /// What the state column says for a cabinet with <b>no entitlement row at all</b> — FR-13's failure state.
    ///
    /// <para>⚠️ Deliberately its own sentence rather than a blank or an « Expiré »: nobody chose it, no payment
    /// fixes it, and the vendor's action is to find out how a cabinet was created without one. « Sans échéance » is
    /// a different thing entirely and is what a grandfathered cabinet reads.</para>
    /// </summary>
    public const string NoEntitlementLabel = "Aucun abonnement";

    /// <param name="adminEmail">
    /// The cabinet's administrator, resolved by its caller — batched over the page in the list, singly in the fiche.
    /// It is a parameter rather than a member of <see cref="PlatformClinicRow"/> because that record is the one
    /// bounded portfolio JOIN (EC-11), and « which admin is the contact? » is a precedence rule that belongs to
    /// <c>IUserRepository</c> and must not be written a second time in SQL here.
    /// </param>
    public static PlatformClinicRowDto ToDto(PlatformClinicRow row, DateTime clinicToday, string? adminEmail)
    {
        ArgumentNullException.ThrowIfNull(row);

        // A cabinet with no entitlement has no state to derive: the reader answers about a date and a suspension
        // flag, and there are neither. Saying so is the whole point (FR-13).
        var status = row.HasEntitlement
            ? SubscriptionStateReader.Read(
                row.SubscriptionEndsOn,
                row.SubscriptionIsSuspended,
                clinicToday,
                row.LatestCoverKind == SubscriptionPeriodKind.Trial)
            : null;

        return new PlatformClinicRowDto(
            ClinicId: row.ClinicId,
            Name: row.Name,
            City: row.City,
            CreatedAt: row.CreatedAt,
            AdminEmail: adminEmail,
            Plan: row.Plan?.ToString(),
            PlanLabel: row.Plan is { } plan ? SubscriptionLabels.Plan(plan) : null,
            State: status?.State.ToString(),
            StateLabel: status is { } s ? SubscriptionLabels.State(s.State) : NoEntitlementLabel,
            EndsOn: status?.EndsOn,
            DaysRemaining: status?.DaysRemaining,
            Users: row.Users,
            Patients: row.Patients,
            Appointments30d: row.Appointments30d,
            Writes7d: row.Writes7d,
            Writes30d: row.Writes30d,
            ActiveDays30d: row.ActiveDays30d,
            LastWriteAt: row.LastWriteAt,
            LastLoginAt: row.LastLoginAt,
            ClinicCollectedThisMonthDt: row.CollectedThisMonth,
            CountersComputedAt: row.CountersComputedAt);
    }
}
