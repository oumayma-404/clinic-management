using System.Security.Claims;
using ClinicManagement.API.Hubs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Hubs;

/// <summary>
/// Verifies the real-time hub requires an authenticated session (AC-3) and, on connect, joins the
/// caller only to its own clinic's group (AC-2) — resolving the clinic id server-side via the same
/// repository the REST handlers use. An unauthenticated/unresolved connection joins no group.
/// </summary>
public class ClinicHubTests
{
    private static readonly Guid ClinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    // [AC-3] The hub is gated by the same auth as the REST API (bearer JWT, both modes).
    [Fact]
    public void Hub_Is_Marked_Authorize()
    {
        var authorize = typeof(ClinicHub)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .SingleOrDefault();

        Assert.NotNull(authorize);
    }

    // [AC-2] A connected client is added to its own clinic's group (resolved from the principal).
    [Fact]
    public async Task OnConnected_Adds_Connection_To_Its_Clinic_Group()
    {
        var user = User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var groups = new Mock<IGroupManager>();
        var hub = new ClinicHub(users.Object)
        {
            Context = BuildContext("conn-1", user.Id),
            Groups = groups.Object
        };

        await hub.OnConnectedAsync();

        groups.Verify(
            g => g.AddToGroupAsync("conn-1", $"clinic-{ClinicId}", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // [AC-2/AC-3] An unauthenticated connection resolves no clinic and joins no group (defense in depth).
    [Fact]
    public async Task OnConnected_Without_Authenticated_User_Joins_No_Group()
    {
        var users = new Mock<IUserRepository>();
        var groups = new Mock<IGroupManager>();
        var hub = new ClinicHub(users.Object)
        {
            Context = BuildContext("conn-2", userId: null),
            Groups = groups.Object
        };

        await hub.OnConnectedAsync();

        groups.Verify(
            g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static HubCallerContext BuildContext(string connectionId, string? userId)
    {
        var context = new Mock<HubCallerContext>();
        context.Setup(c => c.ConnectionId).Returns(connectionId);
        context.Setup(c => c.ConnectionAborted).Returns(CancellationToken.None);

        var identity = userId is null
            ? new ClaimsIdentity() // no claims → not authenticated
            : new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth");
        context.Setup(c => c.User).Returns(new ClaimsPrincipal(identity));

        return context.Object;
    }
}
