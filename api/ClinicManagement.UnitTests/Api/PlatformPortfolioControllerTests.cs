using System.Reflection;
using ClinicManagement.API.Controllers.Platform;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Platform.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// `PlatformPortfolioController`'s list action, and the one class of defect nothing else in this project could see.
///
/// <para><b>Why it exists.</b> The action shipped without a <c>state</c> parameter, so all four of AC-2.3's
/// entitlement filters were dead end to end: the console sent <c>?state=expired</c>, model binding had nowhere to put
/// it, <c>ListPlatformClinicsQuery.State</c> stayed null and the read narrowed nothing. Every layer was correct — the
/// SQL predicate, the handler's parse, the screen's chips — and <c>PlatformPortfolioQueryTests</c> asserts the
/// <i>handler</i> forwards every filter it is given, which it did. The wire hop was the only thing missing, and a
/// dropped filter fails silently: the list still answers, with more cabinets than asked for.</para>
/// </summary>
public class PlatformPortfolioControllerTests
{
    private static (PlatformPortfolioController Controller, Mock<IMediator> Mediator) Build()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<ListPlatformClinicsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PlatformClinicPageDto>.Success(
                new PlatformClinicPageDto([], 1, 25, 0, 0, false, false, null)));

        return (new PlatformPortfolioController(mediator.Object), mediator);
    }

    // [AC-2.3][AC-2.4][AC-2.5] Every argument reaches the query verbatim. Distinct values throughout, so a
    // transposition is a failure rather than a coincidence.
    [Fact]
    public async Task Every_Filter_Reaches_The_Query()
    {
        var (controller, mediator) = Build();

        await controller.ListClinics(
            dormant: true, state: "expired", q: "Béchir", sort: "endsOn", page: 3, pageSize: 10);

        var sent = Assert.IsType<ListPlatformClinicsQuery>(mediator.Invocations.Single().Arguments[0]);
        Assert.True(sent.Dormant);
        Assert.Equal("expired", sent.State);
        Assert.Equal("Béchir", sent.Q);
        Assert.Equal("endsOn", sent.Sort);
        Assert.Equal(3, sent.Page);
        Assert.Equal(10, sent.PageSize);
    }

    /// <summary>
    /// The derived half: the action must be able to carry <b>every</b> filter the query declares.
    ///
    /// <para>Derived rather than listed, for the reason the test above exists at all — the case above covers today's
    /// six arguments and the next filter added to the query is the one that will be forgotten here. It reads the
    /// property set off the request type, so a seventh is covered on the day it is written.</para>
    /// </summary>
    [Fact]
    public void Every_Filter_The_Query_Declares_Has_A_Parameter_To_Arrive_On()
    {
        var declared = typeof(ListPlatformClinicsQuery)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => p.Name)
            .ToList();

        // A reflection guard fails open: a renamed request type would leave this passing for ever while checking
        // nothing, so the candidate set is asserted non-empty before it is used.
        Assert.NotEmpty(declared);

        var parameters = typeof(PlatformPortfolioController)
            .GetMethod(nameof(PlatformPortfolioController.ListClinics))!
            .GetParameters()
            .Select(p => p.Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unreachable = declared.Where(name => !parameters.Contains(name)).ToList();

        Assert.True(
            unreachable.Count == 0,
            $"ListClinics cannot receive: {string.Join(", ", unreachable)}. A filter with no parameter binds to "
                + "nothing and narrows nothing, and the list still answers — with more cabinets than were asked for.");
    }
}
