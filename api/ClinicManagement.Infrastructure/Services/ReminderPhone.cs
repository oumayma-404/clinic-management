using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Reminder-engine phone helpers: normalization to Tunisian <c>+216</c> E.164 + PII masking. The
/// normalization rule is the Domain's single source of truth (<see cref="PhoneNumber.ToE164"/>) — this
/// delegates so patient-entry validation and reminder dispatch stay in lockstep (reliability-and-polish AC-5).
/// </summary>
public static class ReminderPhone
{
    /// <summary>
    /// Accepts common local forms (<c>20 123 456</c>, <c>+216 20 123 456</c>, <c>0021620123456</c>,
    /// <c>216-20-123-456</c>) and returns <c>+216XXXXXXXX</c>; returns <c>null</c> for empty/unparseable input.
    /// Delegates to <see cref="PhoneNumber.ToE164"/> (the shared Domain rule).
    /// </summary>
    public static string? ToE164(string? raw) => PhoneNumber.ToE164(raw);

    /// <summary>
    /// Masks a phone number for logging (PII) — keeps only the last 3 digits, e.g. <c>+21620123456</c> →
    /// <c>*********456</c>. Returns <c>"(none)"</c> for null/empty.
    /// </summary>
    public static string Mask(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return "(none)";
        }

        return phone.Length <= 3 ? "***" : new string('*', phone.Length - 3) + phone[^3..];
    }
}
