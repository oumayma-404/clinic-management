namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Normalizes a stored patient phone number to Tunisian <c>+216</c> E.164, or <c>null</c> when it cannot
/// be parsed as a Tunisian 8-digit number. No I/O — kept separate so it is trivially unit-testable.
/// </summary>
public static class ReminderPhone
{
    /// <summary>
    /// Accepts common local forms (<c>20 123 456</c>, <c>+216 20 123 456</c>, <c>0021620123456</c>,
    /// <c>216-20-123-456</c>) and returns <c>+216XXXXXXXX</c>; returns <c>null</c> for empty/unparseable input.
    /// </summary>
    public static string? ToE164(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());

        if (digits.StartsWith("00216", StringComparison.Ordinal))
        {
            digits = digits[5..];
        }
        else if (digits.Length == 11 && digits.StartsWith("216", StringComparison.Ordinal))
        {
            // An 11-digit "216XXXXXXXX" is a country-code-prefixed number; strip the code to the 8 national
            // digits. (A bare 8-digit national number that merely starts with "216" is length 8, not 11, so
            // it is correctly left intact by this guard.)
            digits = digits[3..];
        }

        return digits.Length == 8 ? "+216" + digits : null;
    }
}
