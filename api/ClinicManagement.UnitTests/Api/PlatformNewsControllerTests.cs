using ClinicManagement.API.Controllers.Platform;
using ClinicManagement.API.Models;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Application.Features.Platform.Dtos;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The status each news refusal reaches the console with — the console branches on the code, the operator reads
/// the status. The successful send queues a Hangfire job, so only its refusals are driven here.
/// </summary>
public class PlatformNewsControllerTests
{
    private static readonly PlatformNewsDraftRequest Draft = new("Objet", [new PlatformNewsItemRequest("🚀", "x")]);

    private static PlatformNewsController Controller<TResponse>(Result<TResponse> answer)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<IRequest<Result<TResponse>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(answer);
        return new PlatformNewsController(mediator.Object) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
    }

    private static int? Status(ActionResult result) => (result as ObjectResult)?.StatusCode;

    [Theory]
    [InlineData(PlatformNewsRules.EmailNotConfiguredCode, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(SendPlatformNewsTestCommandHandler.SendFailedCode, StatusCodes.Status502BadGateway)]
    [InlineData(PlatformNewsRules.InvalidCode, StatusCodes.Status400BadRequest)]
    public async Task A_Refused_Test_Reaches_The_Console_With_A_Status_Matching_Its_Code(string code, int status)
    {
        var controller = Controller(Result<PlatformNewsTestSentDto>.Failure("refusé", code));

        var response = await controller.SendTest(Draft, CancellationToken.None);

        Assert.Equal(status, Status(response.Result!));
    }

    [Theory]
    [InlineData(PlatformNewsRules.EmailNotConfiguredCode, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(PlatformNewsRules.NoAudienceCode, StatusCodes.Status400BadRequest)]
    [InlineData(PlatformNewsRules.InvalidCode, StatusCodes.Status400BadRequest)]
    public async Task A_Refused_Send_Reaches_The_Console_With_A_Status_Matching_Its_Code(string code, int status)
    {
        var controller = Controller(Result<PlatformNewsQueuedDto>.Failure("refusé", code));

        var response = await controller.Send(new SendPlatformNewsRequest(Guid.NewGuid(), "Objet", Draft.Items), CancellationToken.None);

        Assert.Equal(status, Status(response.Result!));
    }
}
