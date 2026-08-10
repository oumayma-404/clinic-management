using System.Reflection;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Subscriptions.Queries;
using ClinicManagement.Infrastructure.Deployment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// `SubscriptionController` — the « Abonnement » read surface (`clinic-subscription` Part C).
///
/// <para>Two properties, and neither is visible in the handlers beside it. The first is that the <b>404 is answered
/// before the mediator is reached</b> (`AuthController`'s `AllowsPublicClinicSignup` precedent): on a deployment that
/// does not work by subscription the handler, its repository and its pricing accessor must never be resolved, or
/// AC-7.1/7.2's « byte for byte as they behave today » becomes « as they behave today, plus two reads ».</para>
///
/// <para>The second is the <b>policy split</b>: the screen is open to every role and only the payment history is
/// admin-only. That is a deliberate exception to this product's « a secretary sees no clinic-wide money screen »
/// rule (AC-2.2), so it is stated here rather than left to be re-derived from an attribute — and it carries a drift
/// guard, because an unclassified new action on this controller is how the exception silently widens.</para>
/// </summary>
public class SubscriptionControllerTests
{
    private static (SubscriptionController Controller, Mock<IMediator> Mediator) For(DeploymentKind kind)
    {
        var mediator = new Mock<IMediator>();
        return (new SubscriptionController(mediator.Object, DeploymentProfile.For(kind)), mediator);
    }

    // ---- the 404, before the mediator ------------------------------------------------------------------

    // [AC-7.1][AC-7.2] Neither other deployment kind has an « Abonnement » screen at all. `SelfHostedLan` is the one
    // that matters most: a clinic's own PC holds a permanent licence, and an endpoint answering « expiré » there
    // would be a refusal invented by a config file.
    [Theory]
    [InlineData(DeploymentKind.SelfHostedLan)]
    [InlineData(DeploymentKind.CloudBrowser)]
    public async Task Both_Reads_Are_Absent_Where_Subscriptions_Are_Not_Enforced(DeploymentKind kind)
    {
        var (controller, mediator) = For(kind);

        Assert.IsType<NotFoundResult>((await controller.GetSubscription()).Result);
        Assert.IsType<NotFoundResult>((await controller.GetHistory()).Result);

        // Not merely « it returned 404 »: nothing behind the endpoint was reached, which is what makes those two
        // deployments observably unchanged rather than changed-but-refusing.
        Assert.Empty(mediator.Invocations);
    }

    // [FR-11] And on the hosted deployment both reads reach the mediator.
    [Fact]
    public async Task Both_Reads_Reach_The_Mediator_On_The_Hosted_Deployment()
    {
        var (controller, mediator) = For(DeploymentKind.HostedMultiTenant);
        mediator.Setup(m => m.Send(It.IsAny<GetSubscriptionQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SubscriptionDto>.Success(new SubscriptionDto()));
        mediator.Setup(m => m.Send(It.IsAny<GetSubscriptionHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SubscriptionHistoryPageDto>.Success(new SubscriptionHistoryPageDto()));

        Assert.IsType<OkObjectResult>((await controller.GetSubscription()).Result);
        Assert.IsType<OkObjectResult>((await controller.GetHistory()).Result);

        Assert.Equal(2, mediator.Invocations.Count);
    }

    // ---- the policy split ------------------------------------------------------------------------------

    // [AC-2.2][EC-10] The screen is `AnyClinicRole` — reception meets the refused save and has to be able to read
    // why, and « Abonnement » is where the refusal's own sentence sends her.
    [Fact]
    public void The_Screen_Is_Open_To_Every_Clinic_Role()
    {
        var policy = typeof(SubscriptionController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy)
            .Single();

        Assert.Equal(AuthorizationPolicies.AnyClinicRole, policy);
    }

    // [AC-2.3] The payment history is the one thing that stays admin-only, tightening the class rather than
    // loosening it — and `GetSubscription` deliberately carries no method-level policy of its own.
    [Fact]
    public void Only_The_Payment_History_Is_Admin_Only()
    {
        Assert.Equal(AuthorizationPolicies.AdminOnly, MethodPolicy(nameof(SubscriptionController.GetHistory)));
        Assert.Null(MethodPolicy(nameof(SubscriptionController.GetSubscription)));
    }

    // A new action here must decide its own policy. Without this, an action added later inherits `AnyClinicRole` —
    // which for a *read of the vendor's money* would widen the AC-2.2 exception by omission.
    [Fact]
    public void Every_Action_Is_Classified_By_This_Test()
    {
        var classified = new[]
        {
            nameof(SubscriptionController.GetSubscription),
            nameof(SubscriptionController.GetHistory),
        };

        var actions = typeof(SubscriptionController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null)
            .Select(m => m.Name)
            .ToList();

        var unclassified = actions.Except(classified).OrderBy(x => x).ToList();

        Assert.True(unclassified.Count == 0,
            "New action(s) on SubscriptionController with no policy decision recorded here: "
            + string.Join(", ", unclassified));
    }

    private static string? MethodPolicy(string action) =>
        typeof(SubscriptionController).GetMethod(action)!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy)
            .SingleOrDefault();
}
