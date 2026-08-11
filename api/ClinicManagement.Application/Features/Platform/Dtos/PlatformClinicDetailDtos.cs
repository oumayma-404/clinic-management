namespace ClinicManagement.Application.Features.Platform.Dtos;

/// <summary>
/// One cabinet, opened (<c>platform-console</c> US-3).
///
/// <para>⚠️ <see cref="Clinic"/> is the <b>same</b> <see cref="PlatformClinicRowDto"/> the list renders, not a
/// second shape carrying the same numbers — AC-3.1 is « the same figures », and two records would drift into a
/// cabinet reading one way in the portfolio and another when opened, which is the hardest kind of discrepancy to
/// notice because both screens look right alone.</para>
///
/// <para>⚠️ <b><see cref="Payments"/> is the companion's own ledger, read (AC-3.2) — never a console-side
/// re-derivation.</b> Every entry is listed, cancelled ones included and marked as such with their reason,
/// canceller and moment: an entry is never edited and never deleted (AC-5.2), so a history that hid them would
/// answer « what were we paid, and for what? » with a curated version of the truth.</para>
/// </summary>
/// <param name="Trend">Six clinic-local months, oldest first (AC-3.1). Always six entries — a month the counter
/// pass never covered is present with <c>DaysMeasured = 0</c> rather than absent, so « pas encore mesuré » and
/// « rien fait » stay distinguishable at the month level exactly as they are at the cabinet level (EC-15).</param>
/// <param name="Payments">Newest first. Empty for a cabinet whose ledger genuinely holds nothing — which, since
/// every cabinet is provisioned with an opening entry (FR-13), means only one that has none at all.</param>
public record PlatformClinicDetailDto(
    PlatformClinicRowDto Clinic,
    string? AdminName,
    string? AdminEmail,
    bool AdminIsActive,
    IReadOnlyList<PlatformActivityMonthDto> Trend,
    IReadOnlyList<PlatformSubscriptionEntryDto> Payments);

/// <summary>
/// One entry of a cabinet's subscription ledger, as the console shows it (AC-3.2).
///
/// <para>⚠️ <see cref="CoversFrom"/>/<see cref="CoversThrough"/> are <b>derived by the fold</b>
/// (<c>SubscriptionLedger.FoldWithSpans</c>), never stored and never recomputed here — the same spans the
/// cabinet's own « Abonnement » screen shows, so vendor and cabinet cannot be told different things about the same
/// payment. A cancelled entry covers nothing and both are null; an open-ended one has no through-day.</para>
///
/// <para>⚠️ <see cref="AmountDt"/> is null for a complimentary period (AC-4.8) — <b>not</b> zero. « Offert » and
/// « payé 0,000 DT » are different statements, and only one of them is ever true.</para>
///
/// <para>⚠️ <see cref="IfCancelled"/> is AC-5.3's consequence, carried on the row it is about and <b>null on a row
/// already cancelled</b> — see <see cref="PlatformCancellationPreviewDto"/>.</para>
/// </summary>
public record PlatformSubscriptionEntryDto(
    Guid EntryId,
    string Kind,
    string KindLabel,
    DateTime RecordedOn,
    DateTime? CoversFrom,
    DateTime? CoversThrough,
    decimal? AmountDt,
    string? Method,
    string? MethodLabel,
    string? Reference,
    string? Note,
    string? RecordedBy,
    bool IsCancelled,
    DateTime? CancelledAt,
    string? CancelledBy,
    string? CancelReason,
    PlatformCancellationPreviewDto? IfCancelled);

/// <summary>
/// Where the cabinet would stand if <i>this</i> entry were cancelled (<c>platform-console</c> AC-5.3, EC-7) — the
/// sentence the confirmation has to say before the vendor commits.
///
/// <para><b>⚠️ Every field is produced by the companion's own machinery and none of it is estimated here</b> (FR-4):
/// the ledger is re-folded through <c>SubscriptionLedger.FoldWithSpans</c> with this one entry marked cancelled, and
/// the resulting date is read by <c>SubscriptionStateReader</c> — the same fold and the same FR-1 rule the write will
/// use a moment later. A console-side « end date minus the duration » would be a second arithmetic, and it is wrong
/// whenever the entry is not the latest one.</para>
///
/// <para><b>⚠️ It travels on the detail read rather than behind a preview endpoint of its own</b>, so the
/// confirmation cannot exist without it: a preview that can fail would leave the dialog either blocking a
/// legitimate correction or opening with AC-5.3's sentence missing. It is as fresh as the page, and the write
/// re-folds and reports the true outcome regardless (progress.md DEV-17).</para>
/// </summary>
/// <param name="EndsOn">The end day the remaining entries fold to, or null for « sans échéance ».</param>
/// <param name="MakesReadOnly">
/// EC-7's headline: <c>true</c> when the cabinet could no longer record new work. Derived as
/// <c>!AllowsWrites</c> from the one FR-1 rule, never by comparing dates here — and it stays <c>true</c> for a
/// cabinet that is <b>suspended</b>, where cancelling changes nothing about the refusal it is already meeting.
/// </param>
public record PlatformCancellationPreviewDto(
    DateTime? EndsOn,
    string State,
    string StateLabel,
    bool MakesReadOnly);

/// <summary>
/// What cancelling an entry answers with (<c>platform-console</c> AC-5.3): where the cabinet stands afterwards, read
/// back through <c>SubscriptionStateReader</c> rather than assumed from the preview the vendor confirmed.
///
/// <para>⚠️ <see cref="PreviousEndsOn"/> is the date the cabinet held a moment ago, so a correction that moved it
/// <b>into the past</b> is legible on the screen that did it — which is the whole of EC-7 after the fact.</para>
/// </summary>
public record PlatformSubscriptionCancelledDto(
    Guid ClinicId,
    Guid EntryId,
    DateTime? PreviousEndsOn,
    DateTime? EndsOn,
    string State,
    string StateLabel,
    int? DaysRemaining,
    bool MakesReadOnly);

/// <summary>
/// What recording a payment answers with (<c>platform-console</c> AC-4.3): the state and the end date the cabinet
/// now stands on, read back through <c>SubscriptionStateReader</c> rather than inferred from « c'est payé ».
///
/// <para>⚠️ <see cref="PreviousEndsOn"/> is what makes EC-3 legible on the screen that did it — « paying early
/// never costs days » is only checkable against the date the cabinet held a moment ago. Null on a replay, where
/// that date is no longer recoverable and a guess would report a period that moved by nothing.</para>
///
/// <para>⚠️ <see cref="AlreadyRecorded"/> is a <b>success</b>, not a refusal (AC-4.6): the second tap of a
/// double-click found the money already taken, which is the outcome the vendor wanted. The screen says so instead
/// of claiming to have taken it twice.</para>
/// </summary>
public record PlatformSubscriptionRecordedDto(
    Guid ClinicId,
    Guid? EntryId,
    DateTime? PreviousEndsOn,
    DateTime? EndsOn,
    string State,
    string StateLabel,
    int? DaysRemaining,
    bool AlreadyRecorded);

/// <summary>
/// One month of a cabinet's activity, summed from its <c>ClinicActivityDay</c> rows.
///
/// <para>⚠️ <see cref="DaysMeasured"/> is the denominator, and it is what stops the trend lying on a young
/// deployment. The counter pass writes a rolling 30-day window, so five of these six months hold no rows at all
/// until the product has been running six months — and a chart drawing those as zero would show every cabinet
/// collapsing from « active » to « dead » the further back you look.</para>
/// </summary>
/// <param name="MonthLabel">« août 2026 » — built server-side, in French, so the chart's axis, its accessible
/// text alternative and any later export cannot disagree about what a bucket is called.</param>
public record PlatformActivityMonthDto(
    int Year,
    int Month,
    string MonthLabel,
    int Writes,
    int Appointments,
    int PatientsCreated,
    int DaysMeasured);
