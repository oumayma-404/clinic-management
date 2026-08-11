namespace ClinicManagement.Application.Features.Platform.Dtos;

/// <summary>
/// One cabinet as the portfolio list shows it (<c>platform-console</c> AC-2.1).
///
/// <para>⚠️ <b>Every member is a count, a date or a total</b> (AC-2.6). Nothing here can name a patient, an
/// appointment, a document or a per-patient amount, and that is not a convention — <c>PlatformReadShape</c>
/// declares the closed set of names this surface may return and <c>PlatformReadShapeTests</c> fails the build on
/// a leaf outside it. Adding <c>PatientName</c> to this record does not compile past the suite.</para>
///
/// <para>⚠️ <b><see cref="State"/> is null for a cabinet that has no entitlement row at all</b> — FR-13's failure
/// state — and <see cref="StateLabel"/> then says so in words. It is <b>not</b> the same as « sans échéance », which
/// is an entitlement whose <see cref="EndsOn"/> is null: reading the two as one would report a grandfathered
/// arrangement as a fault, and a fault as an arrangement.</para>
/// </summary>
/// <param name="State">
/// One of <c>SubscriptionState</c>'s four members, derived by <c>SubscriptionStateReader</c> — the same rule the
/// gate, the cabinet's own screen, the banner and the warning job read, so the console cannot be the one place that
/// answers « is this cabinet expired? » differently. Null where there is no entitlement.
/// </param>
/// <param name="DaysRemaining">
/// Whole clinic-local days left, <b>0 on the last working day</b>. Null with no end date and null once the date has
/// passed — a negative countdown is never surfaced.
/// </param>
/// <param name="ClinicCollectedThisMonthDt">What the <b>cabinet</b> collected this month — its own turnover.
/// Never to be confused with <see cref="PlatformSummaryDto.VendorCollectedThisMonthDt"/>, which is the vendor's
/// revenue; AC-2.7 requires the two to be labelled apart, and the field names carry that distinction here.</param>
/// <param name="CountersComputedAt">When this row's activity figures were measured, or null where the pass has
/// never covered this cabinet. Null is a distinct statement from zero (EC-15).</param>
/// <param name="AdminEmail">
/// Who to write to at the cabinet — the administrator <c>IUserRepository.GetPrimaryAdminContactsAsync</c> names, the
/// same person the fiche shows (AC-3.3). Null where the cabinet has no admin account at all, which is a fact worth
/// seeing in a list rather than only after opening one.
///
/// <para>⚠️ It is the one member of this row that identifies a <b>person</b>, and it is admissible for the reason
/// <c>PlatformReadShape</c> states beside the name: an admin is the cabinet's own staff account — the party the
/// vendor bills and telephones — never somebody the practice treats. The <c>Admin</c> prefix is what keeps that
/// reviewable; a bare <c>Email</c> would be one careless reuse away from a patient's.</para>
/// </param>
public record PlatformClinicRowDto(
    Guid ClinicId,
    string Name,
    string? City,
    DateTime CreatedAt,
    string? AdminEmail,
    string? Plan,
    string? PlanLabel,
    string? State,
    string StateLabel,
    DateTime? EndsOn,
    int? DaysRemaining,
    int Users,
    int Patients,
    int Appointments30d,
    int Writes7d,
    int Writes30d,
    int ActiveDays30d,
    DateTime? LastWriteAt,
    DateTime? LastLoginAt,
    decimal ClinicCollectedThisMonthDt,
    DateTime? CountersComputedAt);

/// <summary>
/// One page of the portfolio.
///
/// <para><see cref="CountersAsOf"/> is the whole of AC-2.8: the oldest measurement on the page, so the freshness
/// stated on screen is a floor rather than a flattering maximum. Null where <b>no</b> cabinet on the page has
/// ever been measured, which the screen must say out loud — otherwise a portfolio whose pass has never run
/// reads as a portfolio of dormant practices (EC-15).</para>
/// </summary>
public record PlatformClinicPageDto(
    IReadOnlyList<PlatformClinicRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    DateTime? CountersAsOf);

/// <summary>
/// The strip above the list (AC-2.7).
///
/// <para>⚠️ <see cref="VendorCollectedThisMonthDt"/> is the <b>vendor's</b> revenue and is <b>never</b> a sum of
/// the cabinets' own <c>ClinicCollectedThisMonthDt</c>. They measure different money over different rows (FR-2),
/// and one standing in for the other would tell the vendor its practices' turnover was its income.</para>
///
/// <para>⚠️ <see cref="InTrial"/>, <see cref="Active"/>, <see cref="Expired"/>, <see cref="Suspended"/> and
/// <see cref="NoEntitlement"/> are mutually exclusive and sum to <see cref="Clinics"/>.
/// <see cref="ExpiringWithin14Days"/> is a <b>subset</b> of the covered cabinets rather than a sixth bucket, which
/// is the whole point of showing it — the screen labels it as such.</para>
/// </summary>
public record PlatformSummaryDto(
    int Clinics,
    int Dormant,
    int NeverMeasured,
    int InTrial,
    int Active,
    int ExpiringWithin14Days,
    int Expired,
    int Suspended,
    int NoEntitlement,
    decimal VendorCollectedThisMonthDt);
