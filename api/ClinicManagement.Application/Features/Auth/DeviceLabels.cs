namespace ClinicManagement.Application.Features.Auth;

/// <summary>
/// How a device's display name is cleaned before it is stored on a <c>SessionFamily</c>.
///
/// <para><b>This is the only untrusted free text on the sign-in path.</b> Everything else a login carries is
/// consumed and discarded — an address is matched, a password is hashed, a code is verified — but the label is
/// kept, and later rendered back on « Mes appareils » next to a button that ends a session. So it is cleaned
/// once, here, rather than at each of the places that will show it.</para>
///
/// <para>⚠️ <b>The cap is derived from the column, not chosen to look tidy.</b>
/// <c>SessionFamilyConfiguration</c> maps the column at 200 characters, and a longer value would not be
/// rejected in any visible way — EF would hand PostgreSQL a string the column refuses, and a sign-in that had
/// already verified a password and a second factor would fail at the final save with a database error the user
/// cannot act on. Truncating is the kinder answer: nothing about a session's identity depends on the label.</para>
///
/// <para>⚠️ <b>Control characters are removed rather than escaped.</b> The value reaches a list, a notification
/// sentence and — the reason that matters — the audit journal, and a newline or an ANSI escape inside it can
/// forge a second line in a log a person reads to work out what happened. Rendering escapes it; a log file does
/// not. Removing them at the door means every consumer is safe without having to know that.</para>
/// </summary>
public static class DeviceLabels
{
    /// <summary>Matches <c>SessionFamilyConfiguration</c>'s <c>HasMaxLength(200)</c>.</summary>
    public const int MaxLength = 200;

    /// <summary>
    /// The cleaned label, or <c>null</c> when nothing usable was supplied.
    ///
    /// <para>Null is a first-class answer, not a failure: a device that offers no name is ordinary, and
    /// <c>SessionFamily.DeviceLabel</c> is nullable precisely so the list can say « appareil sans nom » rather
    /// than invent one.</para>
    /// </summary>
    public static string? Sanitise(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var cleaned = new string(label.Where(c => !char.IsControl(c)).ToArray()).Trim();

        if (cleaned.Length == 0)
        {
            return null;
        }

        return cleaned.Length <= MaxLength ? cleaned : cleaned[..MaxLength].TrimEnd();
    }
}
