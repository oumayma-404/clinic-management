using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Documents.Queries;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// <c>POST /api/medical-documents/generate-pdf-download</c> — the failure path (<c>adoption-qa-k</c> K9).
/// </summary>
/// <remarks>
/// <para>
/// The action used to return <c>BadRequest($"Error generating PDF: {ex.Message}")</c> — a bare JSON <i>string</i>,
/// not the canonical <c>{ error }</c> body. The client's <c>generatePdfForDownload</c> therefore threw a plain
/// <c>Error</c> instead of an <c>ApiError</c>, and <c>handleDownloadPdf</c> only surfaces a message
/// <c>if (error instanceof ApiError)</c> — so the deliberate French operator messages on this path (a missing or
/// unreadable <c>Assets/BS1.pdf</c>, no system font for the overlay) were <b>structurally unreachable</b>. The
/// dentist got a generic toast for a problem with a named remedy.
/// </para>
/// <para>
/// ⚠️ The canonical body shape itself is pinned by <see cref="ApiControllerBaseTests"/>; this class covers only
/// K9's new decision — <b>which</b> exception is surfaced verbatim. The negative case matters as much as the
/// positive one: an arbitrary exception message is a .NET internal, not French, and can carry a file path or a
/// connection string, so it must NOT reach the browser.
/// </para>
/// </remarks>
public class MedicalDocumentPdfErrorTests
{
    /// <summary>Asserts the canonical <c>{ error }</c> shape and returns the message (same helper as the base tests).</summary>
    private static string? ErrorOf(ActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.NotNull(objectResult.Value);
        var property = objectResult.Value!.GetType().GetProperty("error");
        Assert.NotNull(property); // canonical shape: the key is exactly "error" — no bare string, no envelope
        return (string?)property!.GetValue(objectResult.Value);
    }

    /// <summary>
    /// The controller wired so the render throws <paramref name="thrown"/>. The PDF service is resolved from
    /// <c>HttpContext.RequestServices</c> by the action, so it is registered there rather than injected.
    /// </summary>
    private static MedicalDocumentsController ControllerThatThrows(Exception thrown)
    {
        var mediator = new Mock<IMediator>();
        // The practitioner snapshot is best-effort; a failure must not change which message the caller gets.
        mediator
            .Setup(m => m.Send(It.IsAny<GetPractitionerRenderSnapshotQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PractitionerRenderSnapshotDto>.Failure("no clinic in scope"));

        var pdfService = new Mock<IPdfGenerationService>();
        pdfService
            .Setup(s => s.GeneratePdfFromDocumentDataAsync(
                It.IsAny<MedicalDocumentPdfData>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);

        var services = new ServiceCollection();
        services.AddSingleton(pdfService.Object);

        return new MedicalDocumentsController(mediator.Object, NullLogger<MedicalDocumentsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() },
            },
        };
    }

    private static MedicalDocumentPdfData BulletinData() => new()
    {
        DocumentType = "bulletin-cnam",
        PatientName = "Jean Dupont",
        Content = new Dictionary<string, string>(),
    };

    [Fact] // [K9] The renderer's own French operator message reaches the caller, in the canonical body.
    public async Task An_Operator_Message_Reaches_The_Caller_Verbatim()
    {
        // This is the exact type and shape the three fail-fast messages use (`CnamBs1BulletinRenderer` on a
        // missing/unreadable Assets/BS1.pdf, `Bs1FontResolver` when no OS font is found).
        const string operatorMessage =
            "Le formulaire officiel BS1 est introuvable (Assets/BS1.pdf). Réinstallez l'application.";

        var result = await ControllerThatThrows(new InvalidOperationException(operatorMessage))
            .GeneratePdfForDownload(BulletinData());

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal(operatorMessage, ErrorOf(result));
    }

    [Fact] // [K9] Any other exception is generic — its message must not leak to the browser.
    public async Task An_Arbitrary_Exception_Message_Does_Not_Leak()
    {
        const string internalDetail = @"Npgsql failure: Host=10.0.0.4;Password=hunter2;C:\inetpub\clinic\secret";

        var result = await ControllerThatThrows(new Exception(internalDetail)).GeneratePdfForDownload(BulletinData());

        var error = ErrorOf(result);
        Assert.Equal(ErrorMessages.Generic, error); // the shared constant, not a copy of its text
        Assert.DoesNotContain("hunter2", error);
        Assert.DoesNotContain("inetpub", error);
        Assert.DoesNotContain("Npgsql", error);
    }

    [Fact] // [K9] Never a bare JSON string again — that is what made the messages unreachable client-side.
    public async Task The_Failure_Body_Is_Never_A_Bare_String()
    {
        var result = await ControllerThatThrows(new InvalidOperationException("boom"))
            .GeneratePdfForDownload(BulletinData());

        var value = Assert.IsType<ObjectResult>(result).Value;
        Assert.IsNotType<string>(value);
    }
}
