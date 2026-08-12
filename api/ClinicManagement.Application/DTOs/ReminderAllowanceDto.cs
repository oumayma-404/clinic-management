namespace ClinicManagement.Application.DTOs;

/// <summary>
/// « Forfait de rappels WhatsApp » — what the cabinet has left this Tunisian month (US-2, AC-2.1).
///
/// <para>Readable by <b>every</b> clinic role including a secretary (AC-2.2): the person who meets a refused
/// « Relancer » chairside is usually not the person who pays.</para>
///
/// <para>⚠️ <b><see cref="Measured"/> is the field that keeps « 0 restant » and « nous n'avons pas pu lire » apart</b>
/// (AC-2.4 vs EC-12). A counting row exists for every cabinet every month (FR-1a), so <c>false</c> is a statement
/// about <i>us</i> and the screen must say so rather than render three zeros. A failed read is a third thing again and
/// never reaches this DTO at all — it is a <c>Result.Failure</c>.</para>
/// </summary>
public class ReminderAllowanceDto
{
    /// <summary>The Tunisian calendar month, <c>AAAA-MM</c> — never a UTC one (FR-1).</summary>
    public string Month { get; set; } = string.Empty;

    /// <summary>« août 2026 » — built server-side through <c>ClinicClock.MonthLabelFr</c> with <c>fr-FR</c> pinned.</summary>
    public string MonthLabel { get; set; } = string.Empty;

    /// <summary>
    /// What the vendor allowed this month, or <b>null</b> where the cabinet has no counting row at all
    /// (<see cref="Measured"/> false). Null, never 0: see the ⚠️ on the class.
    /// </summary>
    public int? Allowance { get; set; }

    /// <summary>WhatsApp reminders sent this month, or null where nothing was measured.</summary>
    public int? Consumed { get; set; }

    /// <summary>
    /// <c>max(0, allowance − consumed)</c>, floored (AC-2.1) — a cancelled allocation can put consumption above the
    /// allowance and « −17 rappels » is not a quantity anyone can act on. Null where nothing was measured.
    /// </summary>
    public int? Remaining { get; set; }

    /// <summary>Nothing left to send with. False where nothing was measured — an unknown is not an exhaustion.</summary>
    public bool Exhausted { get; set; }

    /// <summary>
    /// The first day of the next Tunisian month — the date the <b>forfait</b> renews (AC-2.7).
    ///
    /// <para>⚠️ It is a fact about the allowance and <b>not</b> a promise about the held reminders: those are for
    /// visits about a day away, so by the 1st they are refused as obsolete rather than sent (AC-4.2).
    /// <c>MessagingRefusals</c> carries the wording that keeps that straight.</para>
    /// </summary>
    public DateTime ResetsOn { get; set; }

    /// <summary>
    /// False ⇒ this cabinet has no counting row for the month, so <b>never render 0</b> (AC-2.4). It should not
    /// normally happen: the daily pass provisions a row for every cabinet (FR-1a).
    /// </summary>
    public bool Measured { get; set; }

    /// <summary>`NotConnected` | `PendingReview` | `Ready` | `TemplateRefused` | `Suspended` (AC-1.4).</summary>
    public string SenderState { get; set; } = string.Empty;

    /// <summary>The sender state in words — never a colour alone (NFR accessibility).</summary>
    public string SenderStateLabel { get; set; } = string.Empty;

    /// <summary>
    /// The cabinet's own WhatsApp number, masked — or <b>null</b>, which is what it always is today.
    ///
    /// <para>⚠️ Nothing in the product stores a cabinet's WhatsApp <i>number</i>: onboarding keeps Meta's
    /// <c>phone_number_id</c>, which is an opaque id and not a phone. Masking an id into something shaped like
    /// « +216 •• ••• •12 » would be an invented fact on the one screen whose job is to say what is true, so this stays
    /// null until Part 4 reads the number back from Meta.</para>
    /// </summary>
    public string? SenderNumber { get; set; }

    /// <summary>
    /// Where an exhausted cabinet writes to ask for more, from <b>operator configuration</b> (AC-2.7) — never a
    /// per-clinic field, since these are the vendor's own details and identical for every cabinet.
    ///
    /// <para>⚠️ Null means the screen renders <b>no contact route at all</b>, not an empty <c>mailto:</c>. A dead
    /// control is worse than an absent one.</para>
    /// </summary>
    public string? ContactEmail { get; set; }

    /// <summary>Where an exhausted cabinet calls, or null. Same absent-not-empty rule as <see cref="ContactEmail"/>.</summary>
    public string? ContactPhone { get; set; }

    /// <summary>
    /// AC-1.1 — can a cabinet be walked through Meta's guided connection <b>right now</b>? Kind <b>and</b> the
    /// deployment's own Meta credentials (<c>IVendorMessagingAvailability.CanOnboardCabinets</c>).
    ///
    /// <para>⚠️ A separate answer from « does this deployment sell vendor messaging », deliberately, and the whole
    /// reason that seam has two members: an allowance a cabinet cannot yet spend is still a real allowance, so the
    /// section, the figures and the history all stay while only the <b>offer to connect</b> goes away. Collapsing the
    /// two would make a missing <c>Meta:AppId</c> look like a deployment that does not sell messaging — and rendering
    /// the button anyway would be a dead control whose failure the practice cannot act on (§ 0).</para>
    /// </summary>
    public bool CanConnect { get; set; }
}

/// <summary>
/// One past month as the history table shows it (AC-2.3).
/// </summary>
/// <param name="Month">The Tunisian month, <c>AAAA-MM</c>.</param>
/// <param name="MonthLabel">« juillet 2026 ».</param>
/// <param name="Allowance">What was in force <b>that</b> month — the stored snapshot, not today's figure applied backwards (FR-1a).</param>
/// <param name="Consumed">What was sent. <b>0 is a real measured zero</b> and reads « 0 rappel envoyé » (AC-2.4).</param>
/// <param name="Measured">
/// False ⇒ no counting row for that month, which reads « non mesuré » and is a statement about us. A month
/// <b>before</b> the cabinet existed is not in the list at all rather than unmeasured (D-5).
/// </param>
public record ReminderAllowanceMonthDto(
    string Month,
    string MonthLabel,
    int? Allowance,
    int? Consumed,
    bool Measured);

/// <summary>
/// The twelve preceding Tunisian months plus the current one, newest first (AC-2.3).
///
/// <para>⚠️ <b>Floored, not padded</b> (D-5): the list starts at <c>max(the cabinet's creation month, its earliest
/// counting row)</c>, so a practice that opened in June is never shown an « unmeasured » May. A gap <i>inside</i> the
/// range is still « non mesuré », which is exactly right — it means a month we failed to count.</para>
/// </summary>
public record ReminderAllowanceHistoryDto(IReadOnlyList<ReminderAllowanceMonthDto> Months);
