namespace ClinicManagement.API.Models;

/// <summary>
/// The body of « enregistrer un forfait de rappels » (<c>vendor-whatsapp-messaging-quota</c> AC-6.1). The cabinet comes
/// from the route, not from here — one identity per request, and a body that could name a different cabinet from the URL
/// is a disagreement waiting to be resolved the wrong way.
/// </summary>
/// <param name="IdempotencyKey">
/// The console mints one per opened sheet, so the second tap of a double-click carries the first tap's key and produces
/// one entry (AC-6.7). Optional: an unkeyed submission is honoured, it simply has no replay protection.
/// </param>
/// <param name="MessagesPerMonth">
/// A <b>standing</b> monthly forfait, « à partir de maintenant ». Exactly one of this and <paramref name="TopUpMessages"/>
/// is supplied. Zero is legal — « ce cabinet n'envoie pas de rappels WhatsApp » is a decision the vendor may record.
/// </param>
/// <param name="TopUpMessages">A one-off addition to <paramref name="AppliesToMonth"/> alone.</param>
/// <param name="AppliesToMonth">
/// The <c>AAAA-MM</c> month a top-up applies to — the current one or a future one, never a past one (AC-6.5). Refused
/// alongside <paramref name="MessagesPerMonth"/>, whose effective month the <b>server</b> decides (AC-6.4a): there is
/// deliberately no way for a caller to name it.
/// </param>
/// <param name="AmountDt">
/// What the vendor was paid, or <b>absent</b> for a complimentary forfait (AC-6.6). An amount of 0,000 DT is refused
/// rather than read as « offert » — the two are different statements, and only one of them is a transaction.
/// </param>
/// <param name="Method">
/// <c>Transfer</c> | <c>Cash</c> | <c>Cheque</c> | <c>Card</c> — the <b>vendor's</b> own vocabulary, never the clinic's
/// payment methods (FR-2). An unrecognised value is refused, not ignored: this is written into a ledger nobody can edit.
/// </param>
public record RecordMessagingAllowanceRequest(
    string? IdempotencyKey,
    int? MessagesPerMonth,
    int? TopUpMessages,
    string? AppliesToMonth,
    decimal? AmountDt,
    string? Method,
    string? Reference,
    string? Note);

/// <summary>
/// The body of « annuler cette allocation » (AC-7.1). The motif is <b>mandatory</b>: the current month's forfait can
/// fall below what the cabinet has already spent, and « pourquoi ce forfait a-t-il diminué ? » has to stay answerable.
/// </summary>
public record CancelMessagingAllowanceRequest(string? Reason);
