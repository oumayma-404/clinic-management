namespace ClinicManagement.Application.Features.Platform.Dtos;

/// <summary>
/// A cabinet's WhatsApp reminder position as the vendor's console shows it
/// (<c>vendor-whatsapp-messaging-quota</c> AC-8.1) — the current month's figures, the sender and template state, and
/// the full allocation history.
///
/// <para>⚠️ <b>All three figures are nullable, and null means « non mesuré » rather than zero</b> (AC-8.3). One
/// counting row exists per cabinet per Tunisian month (FR-1a), so its absence is a fault on <i>our</i> side — and
/// « 0 rappel envoyé » is a claim about the practice. Rendering the two the same way is the mistake this whole feature
/// keeps designing against, and it is the one that would make the vendor leave a broken counter alone.</para>
///
/// <para>⚠️ <b><see cref="TemplateStatus"/> is null until Part 4 stores one</b>, and that is not the same as
/// <c>NotSubmitted</c>: nothing tracks a per-cabinet template yet, so the honest answer is « we do not know », which is
/// what <c>MessagingSender.From</c> already takes as a nullable argument rather than guessing.</para>
///
/// <para>⚠️ <b><see cref="TemplateCategoryLabel"/> is stated only when the category is not <c>UTILITY</c></b> (FR-7b) —
/// it is the vendor's cost per message having moved, and a field that is usually absent is read as an exception, which
/// is what it is. Never surfaced clinic-side, and it holds no reminders.</para>
/// </summary>
/// <param name="Measured">
/// Whether a counting row exists for the current month. Carried explicitly rather than inferred from the three nulls,
/// so a screen cannot get the distinction wrong by forgetting one of them.
/// </param>
/// <param name="Exhausted">
/// Read off the counting row's own rule, so the console, the clinic card and the outbox gate agree on « épuisé ».
/// <c>false</c> where nothing was measured — an unknown is not an exhaustion.
/// </param>
/// <param name="Entries">Newest first, cancelled ones included and marked (AC-6.2).</param>
public record PlatformMessagingDto(
    string Month,
    string MonthLabel,
    int? Allowance,
    int? Consumed,
    int? Remaining,
    bool Measured,
    bool Exhausted,
    int? StandingAllowance,
    string SenderState,
    string SenderStateLabel,
    string? TemplateStatus,
    string? TemplateStatusLabel,
    string? TemplateCategory,
    string? TemplateCategoryLabel,
    IReadOnlyList<PlatformMessagingEntryDto> Entries);

/// <summary>
/// One entry of a cabinet's allocation ledger, as the console shows it (AC-6.2, AC-8.1).
///
/// <para>⚠️ <see cref="AmountDt"/> is null for a complimentary allocation (AC-6.6), <b>not</b> zero — « offert » and
/// « payé 0,000 DT » are different statements and the write path refuses the second spelling outright.</para>
///
/// <para>⚠️ <see cref="IfCancelled"/> is AC-7.3's consequence, computed <b>server-side</b> by re-folding the real
/// ledger with this one entry marked cancelled, and <b>null on a row already cancelled</b>. It travels on the read for
/// its subscription sibling's reason: the confirmation cannot open without the sentence, which a separate preview call
/// that can fail would allow.</para>
/// </summary>
/// <param name="EffectiveMonth">
/// The <c>AAAA-MM</c> month this entry starts applying in — <b>stated rather than re-derived</b> (AC-6.4a), so the
/// file shows when a lowering takes effect instead of leaving the reader to work it out.
/// </param>
public record PlatformMessagingEntryDto(
    Guid EntryId,
    string Kind,
    string KindLabel,
    int Messages,
    string EffectiveMonth,
    string EffectiveMonthLabel,
    DateTime RecordedOn,
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
    PlatformMessagingCancellationPreviewDto? IfCancelled);

/// <summary>
/// What the cabinet's forfait would become if <i>this</i> allocation were cancelled (AC-7.3) — the sentence the
/// confirmation has to say before the vendor commits.
///
/// <para><b>⚠️ Re-folded from the real ledger with the entry marked cancelled, never estimated.</b> The tempting
/// shortcut — « the current allowance minus this entry's messages » — is wrong for a <i>standing</i> entry, which does
/// not add but <b>replaces</b>: cancelling one hands the month back to whatever earlier standing figure was in force,
/// which may be higher, lower, or absent entirely. Re-folding is also what makes the preview and the write agree by
/// construction rather than by review.</para>
///
/// <para>⚠️ <see cref="Allowance"/> is nullable because cancelling a cabinet's only entry leaves it with <b>no
/// allowance record reaching this month</b> — AC-4.3's state, held under its own reason and its own sentence, not
/// « zéro ».</para>
/// </summary>
/// <param name="Exhausted">
/// AC-7.4's headline: <c>true</c> when the month's consumption would meet or exceed the reduced forfait, so reminders
/// are held from that moment. Nothing is unsent and nothing is clawed back — <see cref="Consumed"/> is untouched by a
/// cancellation, which is why it is shown beside the new figure rather than left implicit.
/// </param>
public record PlatformMessagingCancellationPreviewDto(
    int? Allowance,
    int? Consumed,
    int? Remaining,
    bool Exhausted);

/// <summary>
/// What recording an allocation answers with (AC-6.3/6.4): the entry created, the month it takes effect in, and the
/// current month's figure either side of the write.
///
/// <para>⚠️ <see cref="EffectiveMonth"/> is the whole of AC-6.4a on the wire. A <b>lowering</b> comes back with next
/// month's key and <see cref="AllowanceThisMonth"/> unchanged — which is correct and surprising, so the screen states
/// it rather than leaving a vendor to conclude nothing happened.</para>
///
/// <para>⚠️ <see cref="AlreadyRecorded"/> is a <b>success</b>, not a refusal (AC-6.7): the second tap of a double-click
/// found the allocation already recorded, which is the outcome the vendor wanted. Every other field is then re-read
/// from the first submission's own entry, except <see cref="PreviousAllowanceThisMonth"/>, which is no longer
/// recoverable and is null rather than guessed.</para>
/// </summary>
public record PlatformMessagingAllowanceRecordedDto(
    Guid ClinicId,
    Guid? EntryId,
    string? Kind,
    string? KindLabel,
    string? EffectiveMonth,
    string? EffectiveMonthLabel,
    int? Messages,
    int? PreviousAllowanceThisMonth,
    int? AllowanceThisMonth,
    int? ConsumedThisMonth,
    bool AlreadyRecorded);

/// <summary>
/// What cancelling an allocation answers with (AC-7.4), read back after the re-fold rather than assumed from the
/// preview the vendor confirmed — the ledger may have moved between the page render and the click.
///
/// <para>⚠️ <see cref="ConsumedThisMonth"/> is deliberately reported although nothing here can move it: that is
/// precisely the claim AC-7.4 makes, and echoing the figure is what makes it checkable on the screen that did it.</para>
/// </summary>
public record PlatformMessagingAllowanceCancelledDto(
    Guid ClinicId,
    Guid EntryId,
    int? PreviousAllowanceThisMonth,
    int? AllowanceThisMonth,
    int? ConsumedThisMonth,
    bool ExhaustedThisMonth);
