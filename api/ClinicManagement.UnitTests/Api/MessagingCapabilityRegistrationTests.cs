using System.Text.RegularExpressions;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Messaging.Queries;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.UnitTests.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// EC-16's « no scheduled work » half, asserted against <c>Program.cs</c>'s own source
/// (<c>SubscriptionGateMiddlewareTests</c>' precedent).
///
/// <para><b>Why a source scan.</b> Every other EC-16 assertion in the suite hands a component a mocked
/// <c>IVendorMessagingAvailability</c> answering <c>false</c> and watches it do nothing — which proves the
/// component behaves, and proves nothing about whether the deployment ever asks. <c>MessagingAllowanceJob</c> is
/// registered by a composition-root <c>if</c> that no mock reaches: moved one block out of it, the daily pass runs
/// on a clinic's own Windows PC for ever, provisioning forfaits nobody sells and writing « il vous reste N rappels »
/// into a practice that has no vendor. The job is perfectly correct in isolation and only its <i>gating</i> is
/// wrong, so nothing behavioural can see it — the shape this repo has already paid for at
/// <c>SubscriptionGateMiddleware</c>'s ordering and at the startup migration lock.</para>
/// </summary>
public class MessagingCapabilityRegistrationTests
{
    private static string ProgramSource => File.ReadAllText(
        Path.Combine(SolutionSources.Root().FullName, "ClinicManagement.API", "Program.cs"));

    [Fact]
    public void The_Daily_Messaging_Pass_Is_Registered_Only_Where_The_Deployment_Sells_Vendor_Messaging()
    {
        var program = ProgramSource;
        var guard = program.IndexOf("if (profile.SellsVendorMessaging)", StringComparison.Ordinal);

        Assert.True(guard > 0, "Program.cs no longer gates anything on profile.SellsVendorMessaging.");

        var (blockStart, blockEnd) = BlockAfter(program, guard);

        // Derived, never a literal job id: a registration added later under a different name is covered for free,
        // and « found nothing » cannot read as « nothing was wrong » (SystemWideCallerCoverageTests' lesson).
        var registrations = Regex
            .Matches(program, @"RecurringJob\.AddOrUpdate<[^>]*MessagingAllowanceJob>")
            .Select(m => m.Index)
            .ToList();

        Assert.NotEmpty(registrations);
        Assert.All(registrations, at => Assert.InRange(at, blockStart, blockEnd));
    }

    /// <summary>
    /// The <c>else</c> must drop <b>the same</b> job id the <c>if</c> registers. A reprofiled install keeps its
    /// Hangfire storage, so a mistyped id there leaves the old registration running — the one failure the defensive
    /// branch exists to prevent, and the one it cannot report.
    /// </summary>
    [Fact]
    public void The_Defensive_Removal_Names_The_Same_Job_As_The_Registration()
    {
        var program = ProgramSource;

        var registered = Regex.Match(
            program,
            @"RecurringJob\.AddOrUpdate<[^>]*MessagingAllowanceJob>\s*\(\s*""(?<id>[^""]+)""");
        Assert.True(registered.Success, "The messaging pass is no longer registered by name in Program.cs.");

        var id = registered.Groups["id"].Value;
        Assert.Contains($"RecurringJob.RemoveIfExists(\"{id}\")", program, StringComparison.Ordinal);
    }

    // ---- the endpoints, answering as though they do not exist -----------------------------------------------

    /// <summary>
    /// EC-16's « endpoints answering as though they do not exist », over the <b>real</b>
    /// <see cref="DeploymentProfile"/> rather than a mocked capability — which is the point: every other test in
    /// this feature stubs the seam, so none of them can fail if <c>DeploymentProfile.For</c> ever starts answering
    /// <c>true</c> for the wrong kind.
    ///
    /// <para>⚠️ <c>Assert.Empty(mediator.Invocations)</c> is the assertion, not the 404
    /// (<c>SubscriptionControllerTests</c>' precedent): AC-7.1/7.2 is « byte for byte unchanged », not « unchanged
    /// plus two reads », so a 404 raised <i>after</i> the handler, its repository and the allowance policy were all
    /// resolved would satisfy a status check and miss the requirement.</para>
    /// </summary>
    [Theory]
    [InlineData(DeploymentKind.SelfHostedLan)]
    [InlineData(DeploymentKind.CloudBrowser)]
    public async Task Where_The_Deployment_Does_Not_Sell_Messaging_Both_Clinic_Reads_Are_Absent(DeploymentKind kind)
    {
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var controller = new ClinicsController(
            mediator.Object, new ConfigurationBuilder().Build(), DeploymentProfile.For(kind));

        Assert.IsType<NotFoundResult>(await controller.GetReminderAllowance());
        Assert.IsType<NotFoundResult>(await controller.GetReminderAllowanceHistory());
        Assert.Empty(mediator.Invocations);
    }

    /// <summary>
    /// The other direction, so the two cases above cannot pass by the controller simply always 404ing — which is
    /// what a « fix » that dropped the capability check would look like.
    /// </summary>
    [Fact]
    public async Task Where_It_Does_Sell_Messaging_Both_Reads_Reach_The_Mediator()
    {
        var mediator = new Mock<IMediator>();

        // Stubbed explicitly: an unstubbed Send answers null, the controller dereferences it, and the NRE reads as a
        // capability failure rather than as the missing fixture it is (the UnitTests guide's own gotcha).
        mediator
            .Setup(m => m.Send(It.IsAny<GetReminderAllowanceQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ReminderAllowanceDto>.Failure("stub"));
        mediator
            .Setup(m => m.Send(It.IsAny<GetReminderAllowanceHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ReminderAllowanceHistoryDto>.Failure("stub"));

        var controller = new ClinicsController(
            mediator.Object,
            new ConfigurationBuilder().Build(),
            DeploymentProfile.For(DeploymentKind.HostedMultiTenant));

        await controller.GetReminderAllowance();
        await controller.GetReminderAllowanceHistory();

        Assert.Equal(2, mediator.Invocations.Count);
    }

    /// <summary>The extent of the block opening after <paramref name="from"/>, by brace matching.</summary>
    private static (int Start, int End) BlockAfter(string source, int from)
    {
        var start = source.IndexOf('{', from);
        Assert.True(start > 0, "No block follows the SellsVendorMessaging guard.");

        var depth = 0;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                return (start, i);
            }
        }

        throw new InvalidOperationException("Unbalanced braces after the SellsVendorMessaging guard.");
    }
}
