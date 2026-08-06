using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Resolves the El Fatoora identity one clinic's invoices must be signed and filed with (multi-tenant-cloud
/// US-4): its own qualified certificate and TTN account, falling back to the per-install pair only where the
/// install serves a single clinic (<c>DeploymentProfile.SharesInstallWideTtnIdentity</c>).
///
/// <para><b>This interface exists so the precedence rule has exactly one home.</b> Two consumers need it — the
/// signer wants the certificate, the production TTN client wants the credentials — and a rule written twice is
/// the defect shape this repository has caught a dozen times: the copy that is not updated becomes the one
/// that signs with the wrong key.</para>
///
/// <para>⚠️ <b>Throws <see cref="InvalidOperationException"/> with a French operator message</b> when no usable
/// identity exists — a missing certificate, an unreadable blob, a secret the key ring can no longer decrypt.
/// That is not a new error channel: <c>EInvoiceService</c> already catches exactly this exception and records a
/// transient failure, so the invoice stays <c>Queued</c>, the reason lands on the row, and the backlog is
/// visible in <c>GET /api/outbox</c>. A retry works the moment the operator fixes it.</para>
/// </summary>
public interface ITtnIdentityProvider
{
    Task<ResolvedTtnIdentity> ResolveAsync(Guid clinicId, CancellationToken cancellationToken = default);
}
