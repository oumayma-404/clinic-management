using ClinicManagement.API.Controllers;
using ClinicManagement.API.Models;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.Infrastructure.Deployment;
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
    private static AuthController Controller(Mock<IMediator> mediator, DeploymentKind kind) =>
        new(mediator.Object, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DeploymentProfile.ProfileKey] = kind.ToString()
            })
            .Build());

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
    [InlineData(DeploymentKind.CloudBrowser, true)]
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
    [InlineData(DeploymentKind.CloudBrowser, LocalAuthConfig.CloudMode, false)]
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

    // The action returns an anonymous type, so the wire names are only readable by reflection — which is the
    // point: these are the property names the frontend's `AuthModeDto` mirrors.
    private static T Read<T>(object payload, string property)
    {
        var value = payload.GetType().GetProperty(property);
        Assert.NotNull(value);
        return (T)value!.GetValue(payload)!;
    }
}
