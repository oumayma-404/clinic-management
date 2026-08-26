namespace ClinicManagement.Application.Common;

/// <summary>
/// The one reader for « is this string an e-mail address we may store, and what is its canonical form? » on the
/// anonymous paths.
///
/// <para><b>It exists because the defence below is subtle and was needed in a second place.</b> It began as a
/// private helper inside <c>SignUpClinicCommand</c>, which was correct while that was the only anonymous door
/// taking an address from the internet. <c>RequestPasswordResetCommand</c> is the second, and a copy of this logic
/// there would be the shape this codebase fails in most often: a correct, well-reasoned rule wired to one call
/// site, silently diverging from its twin the first time either is touched.</para>
///
/// <para>⚠️ <b>Parsing and keeping the raw string are not the same thing, and the gap was exploitable.</b>
/// <c>MailAddress</c> accepts the display-name form, so <c>Attaquant &lt;dr@cabinet.tn&gt;</c> parsed happily and
/// was stored verbatim — matching no <c>User</c> row (so every « is this already an account? » guard missed it),
/// unique per variant (so « one row per address » collapsed and unlimited mail could be aimed at one mailbox),
/// and, if verified, producing an account whose e-mail no login form can reproduce. Requiring the parsed address
/// to round-trip the input is what closes all three.</para>
/// </summary>
public static class EmailAddressInput
{
    /// <summary>
    /// Validates the address <b>and returns the canonical form</b>, which is what must be stored. Returns a French
    /// refusal, or null when <paramref name="canonical"/> is usable.
    /// </summary>
    public static string? Read(string value, out string canonical)
    {
        canonical = string.Empty;

        var trimmed = value.Trim();
        if (!System.Net.Mail.MailAddress.TryCreate(trimmed, out var parsed) || parsed == null)
        {
            return "L'adresse e-mail n'est pas valide.";
        }

        if (!string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return "Saisissez uniquement l'adresse e-mail, sans nom ni chevrons.";
        }

        canonical = parsed.Address;
        return null;
    }
}
