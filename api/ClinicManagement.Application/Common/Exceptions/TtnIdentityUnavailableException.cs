namespace ClinicManagement.Application.Common.Exceptions;

/// <summary>
/// This clinic has no usable El Fatoora signing identity (multi-tenant-cloud review findings 6 and 7).
///
/// <para><b>Why it is its own type.</b> Every other reason a dispatch fails is transient — TTN is down, the network
/// dropped, the token expired — and burning one of the invoice's five attempts against those is correct. A missing
/// qualified certificate is not: it is a <b>configuration</b> state that lasts days, so a bounded retry budget spent
/// against it empties in about ten minutes and the note then leaves the outbox <i>permanently</i>, needing a manual
/// re-queue nobody is told to perform. Distinguishing it is what lets <c>EInvoiceService</c> park the row instead —
/// exactly the distinction L3 invented <c>NotificationStatus.Blocked</c> for in the reminder outbox.</para>
///
/// <para>The message is the French operator sentence recorded on the invoice row and read back through
/// <c>GET /api/outbox</c>, so it must name what to provide.</para>
/// </summary>
public class TtnIdentityUnavailableException : InvalidOperationException
{
    public TtnIdentityUnavailableException(string message) : base(message)
    {
    }

    public TtnIdentityUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
