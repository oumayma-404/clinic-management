using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Auth;

/// <summary>
/// demo-cabinet FR-4: every demo visitor is the same account, so « Mes appareils » must never list another
/// visitor's session — while every other deployment keeps listing all of the caller's devices.
/// </summary>
public class GetMySessionsDemoTests
{
    private const string UserId = "demo-dentist";

    private static (GetMySessionsQueryHandler Handler, SessionFamily Mine, SessionFamily Theirs) Harness(
        bool refusesWrites, bool tokenNamesFamily = true)
    {
        var mine = new SessionFamily(UserId, "hash-a", DateTime.UtcNow.AddHours(2), "Visite de démonstration");
        var theirs = new SessionFamily(UserId, "hash-b", DateTime.UtcNow.AddHours(2), "Visite de démonstration");

        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(UserId);
        context.Setup(c => c.GetSessionFamilyId()).Returns(tokenNamesFamily ? mine.Id : null);

        var families = new Mock<ISessionFamilyRepository>();
        families
            .Setup(r => r.GetLiveForUserAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { theirs, mine });

        var demo = new Mock<IDemoPolicy>();
        demo.SetupGet(d => d.RefusesWrites).Returns(refusesWrites);

        return (new GetMySessionsQueryHandler(context.Object, families.Object, demo.Object), mine, theirs);
    }

    [Fact] // [FR-4] a visitor sees their own session, never the other visitors'
    public async Task On_The_Demo_Only_The_Callers_Own_Session_Is_Listed()
    {
        var (handler, mine, _) = Harness(refusesWrites: true);

        var result = await handler.Handle(new GetMySessionsQuery(), CancellationToken.None);

        var device = Assert.Single(result.Value!);
        Assert.Equal(mine.Id, device.Id);
        Assert.True(device.IsCurrent);
    }

    [Fact] // a token naming no session cannot be matched to one, so the demo lists nothing rather than everyone
    public async Task On_The_Demo_A_Token_With_No_Session_Lists_Nothing()
    {
        var (handler, _, _) = Harness(refusesWrites: true, tokenNamesFamily: false);

        var result = await handler.Handle(new GetMySessionsQuery(), CancellationToken.None);

        Assert.Empty(result.Value!);
    }

    [Fact] // every other deployment is unchanged: all of the account's live devices
    public async Task Elsewhere_Every_Live_Session_Of_The_Account_Is_Listed()
    {
        var (handler, mine, theirs) = Harness(refusesWrites: false);

        var result = await handler.Handle(new GetMySessionsQuery(), CancellationToken.None);

        Assert.Equal(new[] { theirs.Id, mine.Id }, result.Value!.Select(d => d.Id));
    }
}
