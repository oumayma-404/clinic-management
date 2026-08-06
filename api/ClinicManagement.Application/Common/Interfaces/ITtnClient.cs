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

    /// <summary>
    /// Submits a signed TEIF under <paramref name="identity"/>'s TTN account. That is the <b>same</b> identity
    /// the document was signed with (multi-tenant-cloud US-4): the orchestrator resolves it once and hands it to
    /// both, so filing one clinic's invoice under another's account is not a state this code can reach.
    /// </summary>
    Task<TtnSubmissionResult> SubmitAsync(
        string signedTeifXml,
        string invoiceNumber,
        ResolvedTtnIdentity identity,
        CancellationToken cancellationToken = default);
}
