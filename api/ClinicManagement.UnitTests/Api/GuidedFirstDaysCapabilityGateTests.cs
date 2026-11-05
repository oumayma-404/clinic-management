using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Feedback.Commands;
using ClinicManagement.Application.Features.Feedback.Queries;
using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The feedback and invitation routes (<c>guided-first-days</c> C and E) answer 404 before the mediator on a clinic's
/// own PC: nobody reads « Retours » there and nobody is invited by link. The mediator is strict, so a 404 raised after
/// a handler ran would fail too.
/// </summary>
public class GuidedFirstDaysCapabilityGateTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder().Build();

    private static FeedbackController Feedback(IMediator mediator, DeploymentKind kind) =>
        new(mediator, DeploymentProfile.For(kind));

    private static AuthController Auth(IMediator mediator, DeploymentKind kind)
    {
        var profile = DeploymentProfile.For(kind);
        return new(
            mediator,
            Configuration,
            profile,
            new SubscriptionPolicy(profile, Configuration),
            new SubscriptionPricing(Configuration),
            new DemoCabinetBuilds());
    }

    private static UsersController Users(IMediator mediator, DeploymentKind kind) =>
        new(mediator, Configuration, DeploymentProfile.For(kind), Mock.Of<IStepUpConfirmations>(), Mock.Of<IClinicContext>());

    [Fact]
    public async Task On_A_Clinics_Own_Pc_The_Feedback_And_Invitation_Routes_Are_Absent()
    {
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        const DeploymentKind kind = DeploymentKind.SelfHostedLan;

        Assert.IsType<NotFoundResult>((await Feedback(mediator.Object, kind).GetPrompt(CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(
            await Feedback(mediator.Object, kind).SetPrompt(new SetFeedbackPromptCommand(), CancellationToken.None));
        Assert.IsType<NotFoundResult>(
            (await Feedback(mediator.Object, kind).Submit(new SubmitFeedbackCommand(), CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(
            await Auth(mediator.Object, kind).PreviewInvitation(new AuthController.InvitationTokenRequest("t")));
        Assert.IsType<NotFoundResult>(
            await Auth(mediator.Object, kind).AcceptInvitation(new AuthController.InvitationAcceptRequest("t", "p")));
        Assert.IsType<NotFoundResult>(await Users(mediator.Object, kind).ResendInvitation("local|u"));
        Assert.Empty(mediator.Invocations);
    }

    // The other direction, so the case above cannot pass by a controller that always 404s.
    [Fact]
    public async Task On_The_Online_Version_The_Feedback_Question_Reaches_The_Mediator()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetFeedbackPromptQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FeedbackPromptDto>.Success(new FeedbackPromptDto(false)));

        var response = await Feedback(mediator.Object, DeploymentKind.HostedMultiTenant).GetPrompt(CancellationToken.None);

        Assert.IsType<OkObjectResult>(response.Result);
    }
}
