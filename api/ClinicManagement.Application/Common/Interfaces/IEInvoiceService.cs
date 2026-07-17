namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Orchestrates one El Fatoora dispatch attempt for a queued invoice (FR-1→FR-5): build TEIF → sign →
/// store the signed XML → submit to TTN → persist the outcome (validated + QR cachet, rejected, or a
/// bounded retry). Best-effort and self-committing — it never throws back to the caller (a failure is
/// recorded on the invoice), so it is safe to call inline from a command or from the outbox job.
/// </summary>
public interface IEInvoiceService
{
    Task ProcessAsync(Guid invoiceId, CancellationToken cancellationToken = default);
}
