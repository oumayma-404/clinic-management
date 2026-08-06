using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Signs a TEIF XML document with a qualified certificate (XAdES/XMLDSig, RSA-SHA256) in-process before
/// submission (FR-2). The certificate is supplied by the caller as a resolved
/// <see cref="ResolvedTtnIdentity"/> — see <see cref="ITtnIdentityProvider"/> for whose it is and why that is
/// no longer a per-install question. Throws <see cref="InvalidOperationException"/> when the certificate
/// cannot be opened or carries no usable private key.
/// </summary>
public interface IEInvoiceSigner
{
    SignedEInvoiceResult Sign(string teifXml, ResolvedTtnIdentity identity);
}
