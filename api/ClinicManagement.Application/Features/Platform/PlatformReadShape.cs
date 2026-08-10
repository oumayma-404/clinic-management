namespace ClinicManagement.Application.Features.Platform;

/// <summary>
/// The <b>closed</b> set of scalar field names the vendor console is allowed to return
/// (<c>platform-console</c> US-7, AC-7.2) — and the whole enforcement of the promise « nous ne pouvons pas voir
/// vos dossiers patients ».
///
/// <para><b>Why a declared surface and not the tenant filter</b> (AC-7.2a, and the mistake this exists to
/// prevent). The query filter answers « whose rows may this request read? ». The console's honest answer is
/// « every cabinet's », because a portfolio is a cross-cabinet read by definition — so the filter is
/// <i>lifted</i> here, by design, through <c>UseSystemWide</c>. Anything that assumed the filter was the
/// guarantee would be assuming a mechanism that is switched off on exactly this surface. The guarantee has to
/// be carried by what the surface may <i>return</i>, which is this.</para>
///
/// <para><b>Why names and not types.</b> A type-level allow-list is satisfied by adding a field to an
/// already-allowed type — which is precisely how a patient name would arrive: not as a new DTO, but as one more
/// property on the row somebody was already editing. <c>PlatformReadShapeTests</c> therefore recurses into every
/// response type reachable from a <c>Features.Platform</c> request and checks each <b>leaf</b> against this set,
/// so the failing change is the one-line one.</para>
///
/// <para>⚠️ <b>Adding a name here is the review.</b> There is no exemption mechanism and no attribute to opt out
/// of the check: the only way past it is an edit to this file, which is a diff a reviewer sees and can refuse.
/// Before adding one, the question is not « is this field useful? » but « can this field name, or ever hold, a
/// patient, an appointment, a document, a note, or a per-patient amount? » — AC-2.6 is a promise about the
/// screen, not about intent.</para>
/// </summary>
public static class PlatformReadShape
{
    /// <summary>
    /// Every scalar leaf the console may return, across the sign-in surface and the portfolio.
    ///
    /// <para>Ordinal comparison, deliberately: a case-insensitive set would let <c>patientName</c> and
    /// <c>PatientName</c> be the same declaration, and the point is that each name is written out once by a
    /// person who thought about it.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedLeafNames = new HashSet<string>(StringComparer.Ordinal)
    {
        // ── Sign-in (US-1). A session and, once, the recovery codes.
        "Token",
        "ExpiresAt",
        "RecoveryCodesRemaining",
        "RecoveryCodes",

        // ── A cabinet's identity. Its own name and city — never a person's.
        "ClinicId",
        "Name",
        "City",
        "CreatedAt",

        // ── Its entitlement (null until the companion ships — see PlatformSubscriptionPlaceholder).
        "Plan",
        "State",
        "EndsOn",
        "DaysRemaining",

        // ── Its activity. Counts and dates only: how many, how recently, never what.
        "Users",
        "Patients",
        "Appointments30d",
        "Writes7d",
        "Writes30d",
        "ActiveDays30d",
        "LastWriteAt",
        "LastLoginAt",
        "CountersComputedAt",

        // ── Money. Two totals that must never be read as each other (AC-2.7); the names carry the distinction.
        "ClinicCollectedThisMonthDt",
        "VendorCollectedThisMonthDt",

        // ── The portfolio's own shape: paging, freshness, and the admission that entitlement is not here yet.
        "Items",
        "Page",
        "PageSize",
        "TotalCount",
        "TotalPages",
        "HasPreviousPage",
        "HasNextPage",
        "CountersAsOf",
        "SubscriptionDataAvailable",

        // ── Portfolio-wide counts behind the summary strip.
        "Clinics",
        "Dormant",
        "NeverMeasured",
        "InTrial",
        "Active",
        "ExpiringWithin14Days",
        "Expired",
        "Suspended",

        // ── One cabinet, opened (Part 3, US-3). The row above, verbatim, plus what only the detail shows.
        "Clinic",
        "SubscriptionExplanation",

        // Who to call at the cabinet (AC-3.3). ⚠️ These are the two names on this surface that identify a PERSON,
        // so they are the ones to think hardest about — and they are admissible for a reason the field names carry:
        // an `Admin*` is the cabinet's own **staff account**, the party the vendor bills and telephones, never
        // somebody the practice treats. AC-7.1 forbids a patient, an appointment, a note, a document or a
        // per-patient amount; it does not forbid the account that signed the contract. The prefix is what keeps
        // that reviewable: a bare "Email" would be one careless reuse away from a patient's.
        "AdminName",
        "AdminEmail",
        "AdminIsActive",

        // The six-month trend (AC-3.1). Counts per month and how many days of it were measured — never a per-day
        // list of what was done, which is a different and much sharper read.
        "Trend",
        "Year",
        "Month",
        "MonthLabel",
        "Writes",
        "Appointments",
        "PatientsCreated",
        "DaysMeasured",

        // ── The console's own access ledger (Part 3, FR-5). Its subject is a CONSOLE ACCOUNT and a cabinet, so
        // nothing here can name anyone at the practice at all.
        "Actors",
        "EntryId",
        "PlatformAccountId",
        "AccountEmail",
        "ClinicName",
        "Action",
        "ActionLabel",
        "OccurredAt"
    };
}
