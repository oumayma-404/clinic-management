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
        // hosted-security-hardening FR-1.9. The password floor, so the console states the server's number rather
        // than its own. A policy constant — it names no cabinet, no account and nobody a practice treats.
        "PasswordMinLength",

        // ── A cabinet's identity. Its own name and city — never a person's.
        "ClinicId",
        "Name",
        "City",
        "CreatedAt",

        // ── Its entitlement (Part 4). A forfait, a state, a date and a countdown — all four are facts about the
        // contract between the vendor and the practice, and none of them can name anybody the practice treats.
        "Plan",
        "PlanLabel",
        "State",
        "StateLabel",
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

        // ── The portfolio's own shape: paging and freshness.
        "Items",
        "Page",
        "PageSize",
        "TotalCount",
        "TotalPages",
        "HasPreviousPage",
        "HasNextPage",
        "CountersAsOf",

        // ── Portfolio-wide counts behind the summary strip.
        "Clinics",
        "Dormant",
        "NeverMeasured",
        "InTrial",
        "Active",
        "ExpiringWithin14Days",
        "Expired",
        "Suspended",
        "NoEntitlement",

        // ── One cabinet, opened (Part 3, US-3). The row above, verbatim, plus what only the detail shows.
        "Clinic",

        // ── The cabinet's subscription ledger (Part 4, AC-3.2) — the VENDOR's money, and deliberately a separate
        // vocabulary from the clinic's own (FR-2). Every name here describes a payment the practice made to us: what
        // it bought, when, how much, by what means and under what reference. None of it can reach a patient — the
        // rows come from `SubscriptionPeriods`, a table whose only link to a cabinet is its id.
        "Payments",
        "Kind",
        "KindLabel",
        "RecordedOn",
        "CoversFrom",
        "CoversThrough",
        "AmountDt",
        "Method",
        "MethodLabel",
        "Reference",
        "Note",
        "RecordedBy",
        "IsCancelled",
        "CancelledAt",
        "CancelledBy",
        "CancelReason",

        // ── What a recorded payment answers with (Part 4, AC-4.3): the entry it created and the dates either side
        // of it, so « paying early never costs days » (EC-3) is legible on the screen that did it.
        "PreviousEndsOn",
        "AlreadyRecorded",

        // ── What cancelling one entry would do, and then did (Part 5, AC-5.3, EC-7). Both are facts about the
        // ENTITLEMENT — a date and whether the practice may still record — so neither can name anybody it treats;
        // `EndsOn`, `State` and `StateLabel` above are reused verbatim rather than duplicated under new names,
        // because they mean exactly what they mean everywhere else on this surface.
        "IfCancelled",
        "MakesReadOnly",

        // ── Suspension (Part 6, AC-6.1/6.3/6.4). ⚠️ `SuspensionReason` is the only FREE TEXT this surface returns,
        // so it is the name to think hardest about after the two `Admin*` ones below. It is admissible because of who
        // writes it and about whom: the *vendor* types it about a *practice* (« facturation frauduleuse signalée »),
        // it is refused unless a suspension is being recorded, and no clinic user can reach the field at all — the
        // entitlement is written only by the five vendor verbs and by this console. It can no more name a patient
        // than an invoice's `Reference` can. `SuspendedBy` is a console account id (`console|…`), never a person at
        // the practice.
        "Suspension",
        "SuspensionReason",
        "SuspendedAt",
        "SuspendedBy",
        "IsSuspended",

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

        // ── Re-creating a cabinet from its own archive (`clinic-data-archive-and-restore`). ⚠️ `OneTimePassword` is
        // the credential of the ADMINISTRATOR ACCOUNT the vendor just minted for the practice — it unlocks no
        // record that existed before, it is shown once and never stored readably, and the account must change it
        // on first use; `platform-account create` and `provision-clinic` already answer this way. Everything else
        // here is arithmetic about the restore: `Entity` is a TABLE's name (« Patient », « Invoice »), never a
        // person's, and the three counts beside it are rows. The per-entity rows are a declared shape rather than
        // the report's own dictionary precisely so no `Key`/`Value` pair is pre-approved on this surface.
        //
        // ⚠️ `Warnings` is French prose the SERVER composes about files and tables it could not put back — it can
        // quote a storage key, which is a GUID, and nothing a practice typed. That was **false when written**:
        // the report concatenated the uploaded manifest's own warnings, so whoever supplied the archive controlled
        // unbounded prose here — a patient's name included, which is exactly what a closed set of field names is
        // for. The restorer no longer carries them; this entry is only sound because of that.
        "OneTimePassword",
        "ArchivedAtUtc",
        "Tables",
        "Entity",
        "EntityLabel",
        "Restored",
        "AlreadyPresent",
        "Conflicts",
        "BlobsRestored",
        "Warnings",

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
