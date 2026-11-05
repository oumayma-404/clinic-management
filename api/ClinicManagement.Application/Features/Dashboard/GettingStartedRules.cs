using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Features.Dashboard;

/// <summary>
/// The seven « Premiers pas » steps (<c>guided-first-days</c> B), in the order the card lists them.
///
/// <para>⚠️ Mirrored by <c>web/lib/getting-started.ts</c>, which gives each key its label, its video and its action;
/// <c>GettingStartedKeysTests</c> holds the two in both directions, so a step added on one side alone fails the
/// build instead of rendering as a raw key or never rendering at all.</para>
/// </summary>
public static class GettingStartedKeys
{
    public const string SecurityCode = "securityCode";
    public const string Secretary = "secretary";
    public const string PhoneApp = "phoneApp";
    public const string Patients = "patients";
    public const string Appointment = "appointment";
    public const string DentalRecord = "dentalRecord";
    public const string Letterhead = "letterhead";

    public static readonly IReadOnlyList<string> All =
    [
        SecurityCode, Secretary, PhoneApp, Patients, Appointment, DentalRecord, Letterhead,
    ];
}

/// <summary>What the steps are decided from — every fact read once, so the rule below is pure.</summary>
public sealed record GettingStartedFacts(
    bool SecurityCodeSet,
    int ActiveAccounts,
    bool PhoneAppUsed,
    bool HasPatient,
    bool HasAppointment,
    bool HasDentalRecord,
    bool HasLetterhead,
    IReadOnlyCollection<string>? TickedSteps = null);

/// <summary>
/// When « Premiers pas » shows, and when each step is done. Every step ticks itself from data — nothing here is a
/// box somebody ticks, so the card cannot claim a cabinet is set up when it is not — with one deliberate exception
/// (owner's call, 2026-11-04): the person may tick a step BY HAND (<see cref="TickableSteps"/>), « fait, ou au moins
/// compris », because some cannot be done on a cabinet's first days (no séance yet, the paper not scanned). A step
/// ticked by hand says so (<c>DoneByHand</c>), so the card can offer to untick it.
/// </summary>
public static class GettingStartedRules
{
    /// <summary>The card belongs to a cabinet's first month; after it, it is never read again.</summary>
    public const int VisibleDays = 30;

    /// <summary>
    /// The steps a person may tick by hand — every one but the security code, which is not a matter of
    /// understanding: where it is required, signing in is impossible without it.
    /// </summary>
    public static readonly IReadOnlySet<string> TickableSteps =
        new HashSet<string>(GettingStartedKeys.All.Where(k => k != GettingStartedKeys.SecurityCode), StringComparer.Ordinal);

    public static bool IsWithinFirstDays(DateTime clinicCreatedAtUtc, DateTime nowUtc) =>
        nowUtc - clinicCreatedAtUtc < TimeSpan.FromDays(VisibleDays);

    /// <param name="includeSecurityCode">
    /// False where the deployment does not require an administrator's second factor — a step nobody is asked to
    /// do must not stay unticked for ever and keep the card up.
    /// </param>
    public static IReadOnlyList<GettingStartedStepDto> Steps(GettingStartedFacts facts, bool includeSecurityCode)
    {
        var steps = new List<GettingStartedStepDto>(GettingStartedKeys.All.Count);
        foreach (var key in GettingStartedKeys.All)
        {
            if (key == GettingStartedKeys.SecurityCode && !includeSecurityCode)
            {
                continue;
            }

            var byData = IsDone(key, facts);
            var byHand = !byData && TickableSteps.Contains(key) && facts.TickedSteps?.Contains(key) == true;
            steps.Add(new GettingStartedStepDto(key, byData || byHand, byHand));
        }

        return steps;
    }

    private static bool IsDone(string key, GettingStartedFacts facts) => key switch
    {
        GettingStartedKeys.SecurityCode => facts.SecurityCodeSet,
        // A second ACTIVE account: an invitation not yet accepted, or a request still waiting for approval, is not
        // a colleague who can work yet.
        GettingStartedKeys.Secretary => facts.ActiveAccounts >= 2,
        GettingStartedKeys.PhoneApp => facts.PhoneAppUsed,
        GettingStartedKeys.Patients => facts.HasPatient,
        GettingStartedKeys.Appointment => facts.HasAppointment,
        GettingStartedKeys.DentalRecord => facts.HasDentalRecord,
        GettingStartedKeys.Letterhead => facts.HasLetterhead,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Étape « Premiers pas » inconnue."),
    };
}
