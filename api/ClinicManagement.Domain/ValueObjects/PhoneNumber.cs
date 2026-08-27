using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.ValueObjects;

public class PhoneNumber : ValueObject
{
    public string Value { get; private set; }

    private PhoneNumber() { } // For EF Core

    public PhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("Phone number cannot be empty", nameof(phoneNumber));

        Value = phoneNumber.Trim();
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    /// <summary>
    /// Normalizes a phone number to Tunisian <c>+216</c> E.164, or <c>null</c> when it cannot be parsed as a
    /// Tunisian 8-digit number. This is the single source of truth for the "reminder-deliverable phone" rule
    /// (reliability-and-polish AC-5): patient-entry validation uses it here in the Application layer, and the
    /// Infrastructure reminder engine (<c>ReminderPhone.ToE164</c>) delegates to it, so entry validation and
    /// reminder dispatch can never diverge. Accepts common local forms (<c>20 123 456</c>, <c>+216 20 123 456</c>,
    /// <c>0021620123456</c>, <c>216-20-123-456</c>) and returns <c>+216XXXXXXXX</c>.
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
            // digits. (A bare 8-digit national number that merely starts with "216" is length 8, not 11.)
            digits = digits[3..];
        }

        return digits.Length == 8 ? "+216" + digits : null;
    }

    /// <summary>True when <paramref name="raw"/> is a deliverable Tunisian number (see <see cref="ToE164"/>).</summary>
    public static bool IsDeliverable(string? raw) => ToE164(raw) != null;
}



