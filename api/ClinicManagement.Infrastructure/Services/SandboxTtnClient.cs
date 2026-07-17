using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Sandbox implementation of <see cref="ITtnClient"/> (FR-3): validates any well-formed signed TEIF locally
/// and returns a deterministic fake TTN identifier + a receipt, so the whole e-invoicing pipeline is
/// exercisable end-to-end without hitting production TTN. Selected when the clinic's environment is
/// "Sandbox" (the default).
/// </summary>
public class SandboxTtnClient : ITtnClient
{
    private readonly ILogger<SandboxTtnClient> _logger;

    public SandboxTtnClient(ILogger<SandboxTtnClient> logger)
    {
        _logger = logger;
    }

    public string Environment => Clinic.TtnEnvironmentSandbox;

    public Task<TtnSubmissionResult> SubmitAsync(string signedTeifXml, string invoiceNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(signedTeifXml) || !signedTeifXml.Contains("<Signature", StringComparison.Ordinal))
        {
            // Mirror a schema/data rejection: no signature present ⇒ permanent rejection, not a retry.
            return Task.FromResult(TtnSubmissionResult.Rejected("TEIF non signé (sandbox)."));
        }

        // Deterministic identifier derived from the signed content so repeated sandbox runs are stable.
        var ttnId = "TTN-SBX-" + ShortHash(signedTeifXml);
        var receipt =
            "<TtnReceipt environment=\"Sandbox\">" +
            $"<UniqueIdentifier>{ttnId}</UniqueIdentifier>" +
            $"<InvoiceNumber>{System.Security.SecurityElement.Escape(invoiceNumber)}</InvoiceNumber>" +
            "<Status>Validated</Status>" +
            "</TtnReceipt>";

        _logger.LogInformation("Sandbox El Fatoora accepted invoice {InvoiceNumber} as {TtnId}", invoiceNumber, ttnId);
        return Task.FromResult(TtnSubmissionResult.Validated(ttnId, receipt));
    }

    private static string ShortHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes, 0, 8);
    }
}
