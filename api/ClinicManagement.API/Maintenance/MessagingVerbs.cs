using System.Globalization;
using ClinicManagement.Application.Common;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// What the three <c>messaging-*</c> vendor verbs share beyond what <see cref="SubscriptionVerbs"/> already gives them:
/// reading a month argument, and printing a figure that may be « non mesuré »
/// (<c>vendor-whatsapp-messaging-quota</c> US-9).
///
/// <para><b>It deliberately does not restate the container, the cabinet lookup or the actor declaration.</b> Those are
/// <see cref="SubscriptionVerbs"/>' and are reused verbatim — the vendor's verbs must not disagree about what identifies
/// a practice or about how their writes are attributed, and a second <c>BuildProvider</c> is exactly how the two
/// families would drift. This file holds only what is genuinely new.</para>
///
/// <para><b>⚠️ The tenant-scope declaration is not here either</b>, for that file's reason:
/// <c>SystemWideCallerCoverageTests</c> reads each declaration out of its own <c>Maintenance/*Command.cs</c>, so one
/// hidden in a helper would make every verb look silent to the guard that exists to catch a path reading nothing and
/// reporting success.</para>
/// </summary>
internal static class MessagingVerbs
{
    /// <summary>Exit code when a report ran and found cabinets to act on — <c>reconcile-money</c>'s (AC-9.4).</summary>
    public const int FindingsExitCode = 2;

    /// <summary>
    /// Reads <c>--month AAAA-MM</c>, defaulting to the current Tunisian month. Null return means unusable, already
    /// reported.
    ///
    /// <para>⚠️ <b>Parsed exactly and validated through <c>ClinicClock</c></b>, never by trusting the string: a
    /// malformed key does not fail anywhere downstream — it silently matches no month at all, so the report would
    /// answer « aucun forfait » about every cabinet in the deployment and read as a catastrophe rather than a typo.</para>
    ///
    /// <para>⚠️ The default is the <b>clinic's</b> month, not the server's. At 23:30 UTC on 31 July the cabinet is
    /// already in August, and a report labelled with the wrong month is worse than no report.</para>
    /// </summary>
    public static bool TryReadMonth(string[] args, out string monthKey)
    {
        monthKey = ClinicClock.CurrentMonthKey();

        var raw = ConsoleArgs.ReadOption(args, "--month");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var trimmed = raw.Trim();
        if (!ClinicClock.TryParseMonthKey(trimmed, out _, out _))
        {
            Console.Error.WriteLine(
                $"'{raw}' n'est pas un mois valide pour --month (format AAAA-MM attendu, par exemple 2026-07).");
            return false;
        }

        monthKey = trimmed;
        return true;
    }

    /// <summary>
    /// Reads a whole-number count that may legitimately be <b>zero</b> — which
    /// <see cref="SubscriptionVerbs.TryReadPositiveInt"/> cannot do, since every duration it reads must be positive.
    ///
    /// <para>⚠️ Zero is a real standing forfait (« ce cabinet n'envoie pas de rappels WhatsApp ») and is not the same
    /// state as having no allocation at all, so refusing it here would make one of the two unrecordable. A zero
    /// <i>top-up</i> is refused by the domain instead, where the distinction belongs.</para>
    /// </summary>
    public static bool TryReadCount(string[] args, string flag, out int? value)
    {
        value = null;
        var raw = ConsoleArgs.ReadOption(args, flag);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            Console.Error.WriteLine(
                $"'{raw}' n'est pas un nombre valide pour {flag} (entier positif ou zéro attendu).");
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>Money, in the invariant culture: a command line is not localised and « 120.500 » is not 120500.</summary>
    public static bool TryReadAmount(string[] args, out decimal? value)
    {
        value = null;
        var raw = ConsoleArgs.ReadOption(args, "--amount");

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            Console.Error.WriteLine($"'{raw}' n'est pas un montant valide (nombre positif attendu, ex. 45.000).");
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>
    /// A <b>measured</b> count, or the words for there being no counting row — consumption and what is left.
    ///
    /// <para>⚠️ <b>« non mesuré » rather than 0</b>, and this helper exists so no line of any of the three verbs can get
    /// that wrong by interpolating a nullable int: <c>0</c> is a real figure about the practice (« aucun rappel envoyé »)
    /// while null is a statement about <i>us</i>. Printing them the same way is the one mistake this whole feature keeps
    /// designing against.</para>
    /// </summary>
    public static string Count(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "non mesuré";

    /// <summary>
    /// A folded <b>forfait</b>, or the words for having none.
    ///
    /// <para>⚠️ <b>Deliberately NOT <see cref="Count"/>, and this split is a correction rather than a nicety.</b> A null
    /// allowance and a null consumption are two different faults with two different fixes: no allocation reaches the
    /// month (« aucun forfait » — record one) versus no counting row exists for it (« non mesuré » — the daily pass has
    /// not run). Reusing one helper printed « forfait non mesuré » on a cabinet whose real state was the first, which is
    /// precisely the conflation <c>MessagingAllowanceLedger</c> refuses to make one layer down. Found by running the verb
    /// against a closed month, where every cabinet legitimately has no allocation.</para>
    /// </summary>
    public static string Allowance(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "aucun forfait";
}
