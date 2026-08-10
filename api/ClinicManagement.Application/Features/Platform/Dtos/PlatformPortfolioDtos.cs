namespace ClinicManagement.Application.Features.Platform.Dtos;

/// <summary>
/// One cabinet as the portfolio list shows it (<c>platform-console</c> AC-2.1).
///
/// <para>⚠️ <b>Every member is a count, a date or a total</b> (AC-2.6). Nothing here can name a patient, an
/// appointment, a document or a per-patient amount, and that is not a convention — <c>PlatformReadShape</c>
/// declares the closed set of names this surface may return and <c>PlatformReadShapeTests</c> fails the build on
/// a leaf outside it. Adding <c>PatientName</c> to this record does not compile past the suite.</para>
///
/// <para>⚠️ The four subscription members are <b>null until <c>features/clinic-subscription/</c> ships</b> — see
/// <see cref="PlatformSubscriptionPlaceholder"/>. They are nullable rather than defaulted so « pas encore
/// géré ici » can never be rendered as « Essai » or as « expire aujourd'hui ».</para>
/// </summary>
/// <param name="ClinicCollectedThisMonthDt">What the <b>cabinet</b> collected this month — its own turnover.
/// Never to be confused with <see cref="PlatformSummaryDto.VendorCollectedThisMonthDt"/>, which is the vendor's
/// revenue; AC-2.7 requires the two to be labelled apart, and the field names carry that distinction here.</param>
/// <param name="CountersComputedAt">When this row's activity figures were measured, or null where the pass has
/// never covered this cabinet. Null is a distinct statement from zero (EC-15).</param>
public record PlatformClinicRowDto(
    Guid ClinicId,
    string Name,
    string? City,
    DateTime CreatedAt,
    string? Plan,
    string? State,
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
///
/// <para><see cref="SubscriptionDataAvailable"/> is false while the companion feature is unbuilt. It exists so
/// the screen can hide the state filters and explain the « — » column instead of rendering an empty state
/// nobody can account for — the same rule EC-12 applies to an unreadable portfolio.</para>
/// </summary>
public record PlatformClinicPageDto(
    IReadOnlyList<PlatformClinicRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    DateTime? CountersAsOf,
    bool SubscriptionDataAvailable);

/// <summary>
/// The strip above the list (AC-2.7).
///
/// <para>⚠️ <see cref="VendorCollectedThisMonthDt"/> is the <b>vendor's</b> revenue and is <b>never</b> a sum of
/// the cabinets' own <c>ClinicCollectedThisMonthDt</c>. They measure different money, and one standing in for
/// the other would tell the vendor its practices' turnover was its income. It is null until the subscription
/// ledger exists, for the same reason the state column is.</para>
///
/// <para>The five subscription counts are likewise null while <see cref="SubscriptionDataAvailable"/> is false;
/// <see cref="Clinics"/>, <see cref="Dormant"/> and <see cref="NeverMeasured"/> are real today.</para>
/// </summary>
public record PlatformSummaryDto(
    int Clinics,
    int Dormant,
    int NeverMeasured,
    int? InTrial,
    int? Active,
    int? ExpiringWithin14Days,
    int? Expired,
    int? Suspended,
    decimal? VendorCollectedThisMonthDt,
    bool SubscriptionDataAvailable);
