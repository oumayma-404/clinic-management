using ClinicManagement.API.Controllers;
using ClinicManagement.API.Models;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Clinics.Commands;
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
/// Closing self-registration on a hosted deployment (multi-tenant-cloud US-3, Part C).
///
/// <para><b>The defect this is the guard for is a one-word one.</b> <c>POST /api/auth/register</c> was gated on
/// <c>UsesLocalAccounts</c>, which is <c>true</c> in <i>both</i> account-owning profiles — so shipping
/// <c>HostedMultiTenant</c> without this change would have put a six-character clinic code, printed on a settings
/// screen and known to everyone who ever worked at the practice, between the open internet and an account that
/// reads every patient record. It now asks <c>AllowsSelfRegistration</c>.</para>
///
/// <para>Both refusals are reachable with a mediator that is never called: the guard returns before the command
/// is built, which is also what makes it a guard rather than a handler check.</para>
/// </summary>
public class SelfRegistrationGateTests
{
    // The profile is now injected rather than re-resolved from configuration per request, so it is passed
    // explicitly here. Built through DeploymentProfile.For so the capability matrix under test is the real one —
    // and so is the subscription policy: `trialDays` is read from `Subscription:TrialDays` through the very class
    // production resolves, so a fake here could report a figure the deployment would never serve.
    private static AuthController Controller(
        Mock<IMediator> mediator, DeploymentKind kind, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var profile = DeploymentProfile.For(kind);

        return new(mediator.Object, configuration, profile, new SubscriptionPolicy(profile, configuration));
    }

    private static RegisterRequest AnyRegistration() => new()
    {
        Code = "AB12CD",
        Email = "someone@example.tn",
        Password = "correct horse battery",
        FullName = "Quelqu'un",
        Role = "secretary"
    };

    // [US-3] The change itself. SelfHostedLan keeps self-registration (R-2 — a LAN install behaves exactly as
    // before); the other two refuse, and CloudBrowser already did.
    [Theory]
    [InlineData(DeploymentKind.SelfHostedLan, false)]
    [InlineData(DeploymentKind.HostedMultiTenant, true)]
    public async Task Register_404s_exactly_where_self_registration_is_closed(DeploymentKind kind, bool expect404)
    {
        var mediator = new Mock<IMediator>();
        // Any answer will do for the profile that lets the call through — what is being asserted is whether the
        // handler is reached at all, not what it decides.
        mediator.Setup(m => m.Send(It.IsAny<JoinClinicCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicDto>.Failure("code inconnu"));

        var response = await Controller(mediator, kind).Register(AnyRegistration());

        var timesReached = expect404 ? Times.Never() : Times.Once();
        mediator.Verify(m => m.Send(It.IsAny<JoinClinicCommand>(), It.IsAny<CancellationToken>()), timesReached);

        if (expect404)
        {
            // The guard returns before the command is built, which is what makes it a guard rather than a
            // handler check — a refusal inside the handler would have already read the clinic code.
            Assert.IsType<NotFoundResult>(response);
        }
        else
        {
            Assert.IsNotType<NotFoundResult>(response);
        }
    }

    /// <summary>
    /// [US-3] <c>GET /api/auth/mode</c> keeps answering in every profile — it is a value, not a guard, and the
    /// frontend's mode probe has nothing to branch on otherwise. The <c>mode</c> wire value is unchanged.
    /// </summary>
    [Theory]
    [InlineData(DeploymentKind.SelfHostedLan, LocalAuthConfig.LocalMode, true)]
    [InlineData(DeploymentKind.HostedMultiTenant, LocalAuthConfig.LocalMode, false)]
    public void GetMode_reports_the_mode_and_whether_self_registration_is_open(
        DeploymentKind kind, string expectedMode, bool expectedSelfRegistration)
    {
        var controller = Controller(new Mock<IMediator>(), kind);

        var payload = Assert.IsType<OkObjectResult>(controller.GetMode()).Value!;

        Assert.Equal(expectedMode, Read<string>(payload, "mode"));
        Assert.Equal(expectedSelfRegistration, Read<bool>(payload, "selfRegistrationEnabled"));
    }

    /// <summary>
    /// [US-3] ⚠️ The two profiles that report the SAME mode disagree about self-registration — which is exactly
    /// why the field had to be added. The browser learns the mode from the Next server's <c>AUTH_MODE</c>, and
    /// that reads <c>local</c> on a clinic's own PC and on the hosted backend alike, so <c>/join</c> could not
    /// have told them apart. A test that only checked the three rows above would pass on a
    /// <c>selfRegistrationEnabled</c> derived from <c>mode</c>, which is the wrong answer for one of them.
    /// </summary>
    [Fact]
    public void Self_registration_is_not_derivable_from_the_reported_mode()
    {
        var lan = Assert.IsType<OkObjectResult>(
            Controller(new Mock<IMediator>(), DeploymentKind.SelfHostedLan).GetMode()).Value!;
        var hosted = Assert.IsType<OkObjectResult>(
            Controller(new Mock<IMediator>(), DeploymentKind.HostedMultiTenant).GetMode()).Value!;

        Assert.Equal(Read<string>(lan, "mode"), Read<string>(hosted, "mode"));
        Assert.NotEqual(Read<bool>(lan, "selfRegistrationEnabled"), Read<bool>(hosted, "selfRegistrationEnabled"));
    }

    /// <summary>
    /// [clinic-subscription AC-1.3] The signup form has to state the trial <b>before</b> the visitor submits
    /// anything, and this is where it learns how long one is. <c>null</c> where nothing expires: a deployment that
    /// grants no trial must not be made to advertise one.
    /// </summary>
    [Theory]
    [InlineData(DeploymentKind.SelfHostedLan, null)]
    [InlineData(DeploymentKind.HostedMultiTenant, SubscriptionPolicy.DefaultTrialDays)]
    public void GetMode_reports_the_trial_length_exactly_where_a_trial_exists(DeploymentKind kind, int? expected)
    {
        var payload = Assert.IsType<OkObjectResult>(Controller(new Mock<IMediator>(), kind).GetMode()).Value!;

        Assert.Equal(expected, Read<int?>(payload, "trialDays"));
    }

    /// <summary>
    /// [clinic-subscription AC-1.3] ⚠️ <b>It is the operator's configured figure, not a literal.</b> The wizard's
    /// « N jours d'essai gratuit » and the verification e-mail both quote this, so a hardcoded 30 anywhere would be
    /// a promise no code keeps the day somebody sets <c>Subscription:TrialDays</c> — and this product's own landing
    /// copy already says « 2 semaines ». A test pinning only the default would pass on that literal.
    /// </summary>
    [Fact]
    public void The_reported_trial_length_follows_the_configured_value()
    {
        var payload = Assert.IsType<OkObjectResult>(
            Controller(new Mock<IMediator>(), DeploymentKind.HostedMultiTenant, ("Subscription:TrialDays", "14"))
                .GetMode()).Value!;

        Assert.Equal(14, Read<int?>(payload, "trialDays"));
        Assert.NotEqual(SubscriptionPolicy.DefaultTrialDays, Read<int?>(payload, "trialDays"));
    }

    /// <summary>
    /// [clinic-subscription AC-7.3] The companion of the row above, in the other direction: a
    /// <c>Subscription:*</c> key sets the trial's <i>length</i> and can never turn enforcement on. A LAN install
    /// one config edit away from refusing its own patient records is the failure this pins shut.
    /// </summary>
    [Fact]
    public void No_subscription_setting_can_turn_enforcement_on()
    {
        var payload = Assert.IsType<OkObjectResult>(
            Controller(
                new Mock<IMediator>(),
                DeploymentKind.SelfHostedLan,
                ("Subscription:TrialDays", "14"),
                ("Subscription:Enabled", "true"),
                ("Subscription:RequiresSubscription", "true")).GetMode()).Value!;

        Assert.False(Read<bool>(payload, "requiresSubscription"));
        Assert.Null(Read<int?>(payload, "trialDays"));
    }

    // The action returns an anonymous type, so the wire names are only readable by reflection — which is the
    // point: these are the property names the frontend's `AuthModeDto` mirrors.
    private static T Read<T>(object payload, string property)
    {
        var value = payload.GetType().GetProperty(property);
        Assert.NotNull(value);
        return (T)value!.GetValue(payload)!;
    }
}
