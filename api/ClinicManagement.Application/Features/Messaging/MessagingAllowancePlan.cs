using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Messaging;

/// <summary>
/// What one submitted allocation turns out to be: a standing figure or a top-up, and the Tunisian month it starts
/// applying in.
/// </summary>
public sealed record MessagingAllocationPlan(MessagingAllowanceKind Kind, int Messages, string EffectiveMonth);

/// <summary>
/// The single decision behind AC-6.4a: <b>the server</b> works out whether the vendor recorded a standing figure or a
/// top-up, and which month it takes effect in — the caller never chooses.
///
/// <para><b>Shared rather than copied, because there are two doors.</b> The <c>messaging-grant</c> verb and the
/// console's write both submit the same two figures, and « which month does this take effect in? » must not have two
/// answers: one of them would be wrong only in the vendor's favour and only for a lowering, i.e. rarely and
/// invisibly. This is the <c>fixes-dont-propagate</c> shape caught before it exists.</para>
///
/// <para>⚠️ <b>Pure over the ledger.</b> No repository, no clock — the current month key is a parameter — so the
/// standing-vs-top-up rule and both refusals are assertable without a database, which is where every one of them is
/// actually decided.</para>
/// </summary>
public static class MessagingAllowancePlan
{
    public const string NoFormError =
        "Indiquez le forfait à enregistrer : un forfait mensuel, ou un complément ponctuel pour un mois donné.";

    public const string BothFormsError =
        "Un forfait mensuel et un complément ponctuel sont deux enregistrements distincts : "
        + "indiquez l'un ou l'autre, pas les deux.";

    public const string MonthOnStandingError =
        "Un forfait mensuel ne porte pas de mois : son mois d'effet est déterminé par le serveur "
        + "(immédiat s'il augmente le forfait, le mois suivant s'il le diminue).";

    public const string MonthRequiredError =
        "Indiquez le mois du complément au format AAAA-MM (le mois en cours ou un mois à venir).";

    /// <summary>
    /// AC-6.6, refused rather than normalised. « Offert » is recorded by supplying <b>no</b> amount; an amount of
    /// 0,000 DT reads on the cabinet's file as a transaction that happened for nothing, so the two spellings are not
    /// silently merged — the vendor is asked which one they meant.
    /// </summary>
    public const string ZeroAmountError =
        "Un forfait offert ne porte pas de montant : laissez le montant vide plutôt que d'indiquer 0,000 DT, "
        + "sinon la fiche du cabinet affichera un paiement de zéro.";

    /// <summary>AC-6.5's refusal, with its own code so a console can point at the month field rather than the form.</summary>
    public const string PastMonthCode = "messaging_allowance_past_month";

    /// <summary>Names the month asked for and the earliest legal one, so the correction is obvious from the sentence.</summary>
    public static string PastMonthError(string asked, string earliest) =>
        $"Le mois « {ClinicClock.MonthLabelFr(asked)} » est déjà passé : un complément ne peut pas être ajouté à un "
        + "mois écoulé, dont le cabinet a déjà vu le chiffre. Le mois le plus ancien possible est "
        + $"« {ClinicClock.MonthLabelFr(earliest)} ».";

    /// <param name="ledger">
    /// The cabinet's whole allocation ledger. Needed because a standing figure's effective month depends on the
    /// figure already in force (AC-6.4a) — which is precisely why the caller cannot decide it.
    /// </param>
    /// <param name="currentMonthKey">
    /// The Tunisian month « now », from <c>ClinicClock.CurrentMonthKey()</c>. A parameter for
    /// <c>MessagingAllowanceLedger</c>'s reason: an answer that reads the clock cannot be asked about a boundary.
    /// </param>
    public static Result<MessagingAllocationPlan> Decide(
        int? messagesPerMonth,
        int? topUpMessages,
        string? appliesToMonth,
        decimal? amountDt,
        IReadOnlyList<MessagingAllowanceLedgerEntry> ledger,
        string currentMonthKey)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentMonthKey);

        var forms = (messagesPerMonth.HasValue ? 1 : 0) + (topUpMessages.HasValue ? 1 : 0);

        if (forms == 0)
        {
            return Result<MessagingAllocationPlan>.Failure(NoFormError);
        }

        if (forms == 2)
        {
            return Result<MessagingAllocationPlan>.Failure(BothFormsError);
        }

        // AC-6.6, and refused rather than silently nulled: « offert » and « payé 0,000 DT » are different statements
        // about the same allocation, and picking a side for the vendor is how a fiche comes to show a payment nobody
        // made.
        if (amountDt == 0m)
        {
            return Result<MessagingAllocationPlan>.Failure(ZeroAmountError);
        }

        if (messagesPerMonth is { } perMonth)
        {
            if (!string.IsNullOrWhiteSpace(appliesToMonth))
            {
                return Result<MessagingAllocationPlan>.Failure(MonthOnStandingError);
            }

            // AC-6.4a's decision, measured against the STANDING figure in force — not against the folded total, which
            // would read an ordinary raise as a lowering on any month that happens to carry a top-up.
            var effective = MessagingAllowanceLedger.EffectiveMonthFor(
                ledger, perMonth, currentMonthKey, ClinicClock.NextMonthKey(currentMonthKey));

            return Result<MessagingAllocationPlan>.Success(
                new MessagingAllocationPlan(MessagingAllowanceKind.Standing, perMonth, effective));
        }

        var month = appliesToMonth?.Trim();

        if (string.IsNullOrWhiteSpace(month) || !ClinicClock.TryParseMonthKey(month, out _, out _))
        {
            return Result<MessagingAllocationPlan>.Failure(MonthRequiredError);
        }

        // AC-6.5. Ordinal comparison on a zero-padded key, so chronological order needs no parsing (D-7). A past
        // top-up releases nothing — those reminders have already come due and been refused — and it would rewrite a
        // figure the practice has already been shown.
        if (string.CompareOrdinal(month, currentMonthKey) < 0)
        {
            return Result<MessagingAllocationPlan>.Failure(PastMonthError(month, currentMonthKey), PastMonthCode);
        }

        return Result<MessagingAllocationPlan>.Success(
            new MessagingAllocationPlan(MessagingAllowanceKind.TopUp, topUpMessages!.Value, month));
    }
}
