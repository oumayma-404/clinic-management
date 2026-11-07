using System.Reflection;
using ClinicManagement.API.Controllers;
using ClinicManagement.API.Controllers.Platform;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.News;
using ClinicManagement.Application.Features.News.Commands;
using ClinicManagement.Application.Features.News.Queries;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Infrastructure.Deployment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// « Nouveautés » in the app is for administrators, and absent — never empty — where no console writes news; the
/// console's hide/show reaches the command with the direction its route names.
/// </summary>
public class NewsControllerTests
{
    private static NewsController News(IMediator mediator, DeploymentKind kind) =>
        new(mediator, DeploymentProfile.For(kind)) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };

    [Fact]
    public void Only_Administrators_Reach_The_News()
    {
        var policy = typeof(NewsController).GetCustomAttribute<AuthorizeAttribute>()?.Policy;

        Assert.Equal(AuthorizationPolicies.AdminOnly, policy);
    }

    [Fact]
    public async Task Every_Route_Is_Absent_Where_The_Deployment_Serves_No_Console()
    {
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var controller = News(mediator.Object, DeploymentKind.SelfHostedLan);

        Assert.IsType<NotFoundResult>((await controller.List(CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await controller.Summary(CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(await controller.MarkRead(null, CancellationToken.None));
    }

    [Fact]
    public async Task The_Hosted_Deployment_Serves_The_Badge()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetNewsSummaryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<NewsSummaryDto>.Success(new NewsSummaryDto(2)));

        var response = await News(mediator.Object, DeploymentKind.HostedMultiTenant).Summary(CancellationToken.None);

        Assert.Equal(2, Assert.IsType<NewsSummaryDto>(Assert.IsType<OkObjectResult>(response.Result).Value).Unread);
    }

    [Fact]
    public async Task Marking_Read_Answers_No_Content()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<MarkNewsReadCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        Assert.IsType<NoContentResult>(await News(mediator.Object, DeploymentKind.HostedMultiTenant).MarkRead(null, CancellationToken.None));
    }

    [Fact]
    public async Task The_Date_The_Page_Showed_Reaches_The_Command()
    {
        var shown = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<MarkNewsReadCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        await News(mediator.Object, DeploymentKind.HostedMultiTenant)
            .MarkRead(new MarkNewsReadCommand { UpTo = shown }, CancellationToken.None);

        mediator.Verify(m => m.Send(It.Is<MarkNewsReadCommand>(c => c.UpTo == shown), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_Console_Route_Names_The_Direction(bool show)
    {
        var id = Guid.NewGuid();
        SetPlatformNewsVisibilityCommand? sent = null;
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<SetPlatformNewsVisibilityCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<PlatformNewsVisibilityDto>>, CancellationToken>((c, _) => sent = (SetPlatformNewsVisibilityCommand)c)
            .ReturnsAsync(Result<PlatformNewsVisibilityDto>.Success(new PlatformNewsVisibilityDto(id, show)));
        var controller = new PlatformNewsController(mediator.Object) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };

        var response = show ? await controller.Show(id, CancellationToken.None) : await controller.Hide(id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(response.Result);
        Assert.Equal((id, show), (sent!.BroadcastId, sent.Visible));
    }

    [Fact]
    public async Task An_Unknown_News_Is_A_404_In_The_Console()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<SetPlatformNewsVisibilityCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PlatformNewsVisibilityDto>.Failure("introuvable", SetPlatformNewsVisibilityCommandHandler.NotFoundCode));
        var controller = new PlatformNewsController(mediator.Object) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };

        var response = await controller.Hide(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, (response.Result as ObjectResult)?.StatusCode);
    }
}
