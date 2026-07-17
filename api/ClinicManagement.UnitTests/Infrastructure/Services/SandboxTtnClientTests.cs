using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The sandbox TTN « El Fatoora » client (FR-3): the selectable test implementation that makes the whole
/// e-invoicing pipeline exercisable without production TTN. Accepts a well-formed signed TEIF (→ validated
/// with a deterministic identifier + receipt) and rejects an unsigned one.
/// </summary>
public class SandboxTtnClientTests
{
    private static SandboxTtnClient Client() => new(NullLogger<SandboxTtnClient>.Instance);

    private const string SignedTeif = "<TEIF version=\"1.8.8\"><InvoiceBody/><Signature>abc</Signature></TEIF>";
    private const string UnsignedTeif = "<TEIF version=\"1.8.8\"><InvoiceBody/></TEIF>";

    // [FR-3] The sandbox client advertises the Sandbox environment (so the orchestrator selects it there).
    [Fact]
    public void Environment_Is_Sandbox()
    {
        Assert.Equal(Clinic.TtnEnvironmentSandbox, Client().Environment);
    }

    // [FR-3] A signed TEIF is validated with a TTN identifier and a receipt.
    [Fact]
    public async Task SubmitAsync_Validates_Signed_Teif()
    {
        var result = await Client().SubmitAsync(SignedTeif, "2026-0001");

        Assert.Equal(TtnSubmissionOutcome.Validated, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.TtnIdentifier));
        Assert.StartsWith("TTN-SBX-", result.TtnIdentifier);
        Assert.Contains(result.TtnIdentifier!, result.ReceiptContent!);
    }

    // [FR-3] The sandbox identifier is deterministic for the same signed content (stable re-runs).
    [Fact]
    public async Task SubmitAsync_Is_Deterministic()
    {
        var first = await Client().SubmitAsync(SignedTeif, "2026-0001");
        var second = await Client().SubmitAsync(SignedTeif, "2026-0001");

        Assert.Equal(first.TtnIdentifier, second.TtnIdentifier);
    }

    // [Edge: bad data / schema] An unsigned TEIF is rejected (permanent), not retried.
    [Fact]
    public async Task SubmitAsync_Rejects_Unsigned_Teif()
    {
        var result = await Client().SubmitAsync(UnsignedTeif, "2026-0001");

        Assert.Equal(TtnSubmissionOutcome.Rejected, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
