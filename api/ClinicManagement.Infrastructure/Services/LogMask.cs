namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Renders a patient-identifying value safe to write to a log file
/// (<c>hosted-security-hardening</c> FR-4.4).
///
/// <para><b>It extends <see cref="ReminderPhone.Mask"/>'s precedent rather than competing with it.</b> That one
/// masks a phone number and stays where it is — the number is a phone number wherever it appears. This is the
/// same idea for the two other shapes FR-4.4 names: a person's name, and a file name composed from one.</para>
///
/// <para>⚠️ <b>Masking is the fall-back, not the goal.</b> Where the diagnostic handle is genuinely an identity,
/// the right fix is to log the <b>identifier</b> — a patient's <c>Guid</c> tells whoever is debugging exactly
/// which record, is meaningless to anyone who only has the log, and needs no masking at all. This exists for the
/// cases where no identifier is in hand, which on the Google→App path is most of them: the whole difficulty
/// there is that a name arrived from a calendar event and no patient has been resolved from it yet.</para>
///
/// <para>⚠️ <b>Why it does not simply drop the value.</b> « Cannot extract patient name » with nothing after it
/// is unactionable — an operator cannot tell a diacritic problem from an empty summary from a calendar entry
/// that is not a patient at all. The initial plus the length distinguishes those without naming anybody.</para>
/// </summary>
public static class LogMask
{
    /// <summary>
    /// A person's name as <c>M… (7)</c> — the first character and how many there are. Empty is
    /// <c>(none)</c>, matching <see cref="ReminderPhone.Mask"/> so the two read the same way in one log line.
    /// </summary>
    public static string Name(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(none)";
        }

        var trimmed = value.Trim();
        return $"{trimmed[0]}… ({trimmed.Length})";
    }

    /// <summary>
    /// A file name reduced to its extension — <c>*.pdf</c>. <c>DocumentFileNaming</c> composes these from the
    /// patient's name and the document type, so the stem is PHI and the extension is the only part that ever
    /// diagnosed anything (« the attachment was a .docx, not a .pdf »).
    /// </summary>
    public static string FileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(none)";
        }

        var extension = Path.GetExtension(value.Trim());
        return string.IsNullOrEmpty(extension) ? "(sans extension)" : $"*{extension}";
    }
}
