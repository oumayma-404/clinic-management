using System.Net.Mail;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// **Who a document email is sent AS.** One owner, because the answer is three headers and setting any one of
/// them alone is worse than leaving all three as they were.
///
/// <para>
/// Until now every document left the cabinet from the install's own SMTP identity — « our app mail », as it was
/// reported. A patient receiving her ordonnance saw a no-reply address she had never heard of, and a reply to a
/// confrère's lettre de liaison went nowhere a human reads. The practitioner who pressed « Envoyer » is the
/// person the recipient thinks they are corresponding with, so that is the address on the letter.
/// </para>
///
/// <para>
/// ⚠️ **A `From` the SMTP account is not authorised for is refused by most providers**, and that refusal is a
/// 5.x.x — Gmail answers « Mail from must equal authorized user », Microsoft 365 « SendAsDenied ». So the
/// practitioner's address is a *preference*, never an assumption: <see cref="Sender"/> carries the mailbox that
/// actually transmitted the message (RFC 5322 § 3.6.2 — the header that exists for precisely this « sent on
/// behalf of » case), and <see cref="Configured"/> is the retry the sender falls back to, which keeps the
/// practitioner reachable on `Reply-To` even when their address may not appear on `From`.
/// </para>
/// </summary>
/// <param name="FromAddress">The address the recipient sees the mail as coming from.</param>
/// <param name="FromDisplayName">The name beside it, when there is one.</param>
/// <param name="SenderAddress">RFC 5322 <c>Sender</c> — the authenticated mailbox, set only when it differs from <c>From</c>.</param>
/// <param name="ReplyToAddress">Where a reply goes. Always the practitioner when we know them.</param>
/// <param name="ReplyToDisplayName">The name beside the reply address.</param>
public sealed record DocumentEmailIdentity(
    string FromAddress,
    string? FromDisplayName,
    string? SenderAddress,
    string? ReplyToAddress,
    string? ReplyToDisplayName)
{
    /// <summary>True when <c>From</c> is not the account we authenticated as — the case a relay may refuse.</summary>
    public bool SpeaksForSomeoneElse => SenderAddress != null;

    /// <summary>
    /// The practitioner on <c>From</c> when we have a usable address for them, otherwise the cabinet's own
    /// identity unchanged. An unparseable or blank practitioner address is ignored rather than refused: the
    /// document must still go out.
    /// </summary>
    public static DocumentEmailIdentity ForPractitioner(
        string configuredAddress,
        string? configuredName,
        string? practitionerAddress,
        string? practitionerName)
    {
        var practitioner = Usable(practitionerAddress);
        if (practitioner == null)
        {
            return new DocumentEmailIdentity(configuredAddress, Trimmed(configuredName), null, null, null);
        }

        // The same mailbox with a nicer name on it — no `Sender`, and a `Reply-To` pointing at `From` is noise.
        if (string.Equals(practitioner, configuredAddress.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new DocumentEmailIdentity(
                configuredAddress, Trimmed(practitionerName) ?? Trimmed(configuredName), null, null, null);
        }

        return new DocumentEmailIdentity(
            practitioner,
            Trimmed(practitionerName),
            configuredAddress.Trim(),
            practitioner,
            Trimmed(practitionerName));
    }

    /// <summary>
    /// The cabinet's own identity on <c>From</c>, keeping the practitioner on <c>Reply-To</c> — what the sender
    /// retries with when a relay refuses to let us speak for somebody else.
    /// </summary>
    public static DocumentEmailIdentity Configured(
        string configuredAddress,
        string? configuredName,
        string? practitionerAddress = null,
        string? practitionerName = null)
    {
        var practitioner = Usable(practitionerAddress);
        var samePerson = practitioner != null
            && string.Equals(practitioner, configuredAddress.Trim(), StringComparison.OrdinalIgnoreCase);

        return new DocumentEmailIdentity(
            configuredAddress.Trim(),
            Trimmed(configuredName),
            null,
            samePerson ? null : practitioner,
            samePerson ? null : Trimmed(practitionerName));
    }

    /// <summary>The trimmed address if it is one a mail header can carry, else null.</summary>
    private static string? Usable(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        try
        {
            return new MailAddress(address.Trim()).Address;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
