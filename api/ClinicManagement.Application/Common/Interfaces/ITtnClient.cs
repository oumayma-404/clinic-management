using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Provider-abstracted client for the TTN « El Fatoora » platform (FR-3). Submits a signed TEIF and reports
/// the outcome (validated + unique identifier / rejected / transient failure). A sandbox implementation is
/// selectable so the feature is exercisable without hitting production TTN.
/// </summary>
public interface ITtnClient
{
    /// <summary>Which TTN environment this client targets ("Sandbox" or "Production"); the orchestrator
    /// picks the client matching the clinic's configured environment.</summary>
    string Environment { get; }

    Task<TtnSubmissionResult> SubmitAsync(string signedTeifXml, string invoiceNumber, CancellationToken cancellationToken = default);
}
