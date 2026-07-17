using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Signs a TEIF XML document with the clinic's qualified certificate (XAdES/XMLDSig, RSA-SHA256) in-process
/// before submission (FR-2). The certificate + its secret come from the per-install <c>.local/</c> store.
/// Throws <see cref="InvalidOperationException"/> when no usable certificate is configured.
/// </summary>
public interface IEInvoiceSigner
{
    SignedEInvoiceResult Sign(string teifXml);
}
