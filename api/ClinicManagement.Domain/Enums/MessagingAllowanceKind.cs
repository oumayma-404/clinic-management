namespace ClinicManagement.Domain.Enums;

/// <summary>
/// What one entry of a cabinet's WhatsApp reminder allocation ledger does
/// (<c>vendor-whatsapp-messaging-quota</c> FR-2, AC-6.1).
///
/// <para>⚠️ <b>Which of the two an entry is, is decided by the server</b> (AC-6.4a) from the figure already in force
/// for the current month — the vendor states an amount, never an effective month or a kind. That is what makes
/// AC-6.3 (a raise applies at once) and AC-6.4 (a lowering waits for the next Tunisian month) properties of the
/// ledger rather than of whoever typed the form.</para>
/// </summary>
public enum MessagingAllowanceKind
{
    /// <summary>
    /// The cabinet's <b>standing</b> monthly figure, in force from its effective month onwards until another
    /// standing entry supersedes it. One of these is written when a cabinet is provisioned (FR-3).
    /// </summary>
    Standing = 1,

    /// <summary>
    /// A <b>one-off</b> addition to a single named month, on top of whatever standing figure covers it (AC-6.1).
    /// It may name the current or a future month, never a past one (AC-6.5) — a month that has closed cannot be
    /// given messages it could have spent.
    /// </summary>
    TopUp = 2
}
