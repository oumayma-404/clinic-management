namespace ClinicManagement.Application.Features.Auth;

/// <summary>
/// Builds the <c>otpauth://</c> URI an authenticator app scans (<c>hosted-security-hardening</c> FR-1.3).
///
/// <para>⚠️ <b>Nothing in this product produced one before</b> — <c>otpauth</c> had zero occurrences in the
/// repository, because the console's operator types the raw base32 by hand. A dentist will not, so the clinic
/// enrolment ships a QR.</para>
///
/// <para><b>The label carries the practice name AND the account's address</b> (Stated Assumption 4). Somebody who
/// works at two practices otherwise gets two entries in their authenticator reading « Clinic Management », with
/// nothing to say which code belongs to which — and the codes are indistinguishable six-digit numbers.</para>
/// </summary>
public static class TotpEnrolmentUri
{
    /// <summary>
    /// <c>otpauth://totp/{issuer}:{account}?secret=…&amp;issuer={issuer}</c>.
    ///
    /// <para>The issuer appears <b>twice</b> by specification — once as the label prefix, which is what older
    /// apps display, and once as a parameter, which is what current ones read. Emitting only one is the usual
    /// way an entry ends up unlabelled in half the authenticators on the market.</para>
    ///
    /// <para>Every component is percent-encoded, including the colon separator's operands: a practice named
    /// « Cabinet Dr. Ben Salah &amp; Associés » is ordinary, and an unescaped <c>&amp;</c> would truncate the
    /// query string and silently produce a URI whose secret is missing.</para>
    /// </summary>
    public static string Build(string practiceName, string accountEmail, string base32Secret)
    {
        if (string.IsNullOrWhiteSpace(base32Secret))
        {
            throw new ArgumentException("Le secret est obligatoire.", nameof(base32Secret));
        }

        var issuer = Fallback(practiceName, "Clinique");
        var account = Fallback(accountEmail, "compte");

        var label = $"{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}";

        return $"otpauth://totp/{label}"
               + $"?secret={Uri.EscapeDataString(base32Secret)}"
               + $"&issuer={Uri.EscapeDataString(issuer)}";
    }

    /// <summary>
    /// Groups the raw secret in fours for the « saisir manuellement » fallback.
    ///
    /// <para>It is read off a screen and typed into a phone, so an ungrouped 32-character run is where a
    /// transcription error comes from. Authenticators strip whitespace themselves.</para>
    /// </summary>
    public static string ForReading(string base32Secret)
    {
        if (string.IsNullOrWhiteSpace(base32Secret))
        {
            return string.Empty;
        }

        var groups = base32Secret
            .Chunk(4)
            .Select(chunk => new string(chunk));

        return string.Join(" ", groups);
    }

    private static string Fallback(string? value, string whenMissing) =>
        string.IsNullOrWhiteSpace(value) ? whenMissing : value.Trim();
}
