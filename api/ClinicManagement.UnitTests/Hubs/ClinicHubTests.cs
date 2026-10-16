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
/// Since staff-chat it also joins the caller's own group and counts presence.
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
        var (hub, groups, _, user) = Build("conn-1", new StaffPresence());

        await hub.OnConnectedAsync();

        groups.Verify(
            g => g.AddToGroupAsync("conn-1", $"clinic-{ClinicId}", It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.NotNull(user);
    }

    // [AC-2/AC-3] An unauthenticated connection resolves no clinic and joins no group (defense in depth).
    [Fact]
    public async Task OnConnected_Without_Authenticated_User_Joins_No_Group()
    {
        var users = new Mock<IUserRepository>();
        var groups = new Mock<IGroupManager>();
        var hub = new ClinicHub(users.Object, new StaffPresence())
        {
            Context = BuildContext("conn-2", userId: null),
            Groups = groups.Object
        };

        await hub.OnConnectedAsync();

        groups.Verify(
            g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // staff-chat: a message to one person is sent to that person's group, so the hub must put each tab in it.
    [Fact]
    public async Task OnConnected_Joins_The_Callers_Own_Group()
    {
        var (hub, groups, _, user) = Build("conn-3", new StaffPresence());

        await hub.OnConnectedAsync();

        groups.Verify(
            g => g.AddToGroupAsync("conn-3", ClinicGroups.User(ClinicId, user.Id), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // staff-chat AC-1.4: « En ligne » changes only when the first tab opens and the last one closes.
    [Fact]
    public async Task Presence_Is_Signalled_On_The_First_Tab_And_On_The_Last_Close_Only()
    {
        var presence = new StaffPresence();
        var (first, _, firstProxy, user) = Build("tab-1", presence);
        var (second, _, secondProxy, _) = Build("tab-2", presence, user);

        await first.OnConnectedAsync();
        await second.OnConnectedAsync();
        Assert.True(presence.IsOnline(ClinicId, user.Id));
        VerifyPresenceSignal(firstProxy, Times.Once());
        VerifyPresenceSignal(secondProxy, Times.Never());

        await second.OnDisconnectedAsync(null);
        Assert.True(presence.IsOnline(ClinicId, user.Id));
        VerifyPresenceSignal(secondProxy, Times.Never());

        await first.OnDisconnectedAsync(null);
        Assert.False(presence.IsOnline(ClinicId, user.Id));
        VerifyPresenceSignal(firstProxy, Times.Exactly(2));
    }

    private static void VerifyPresenceSignal(Mock<IClientProxy> proxy, Times times) =>
        proxy.Verify(
            p => p.SendCoreAsync(ClinicHub.StaffChatChanged, It.Is<object?[]>(a => a.Length == 0), It.IsAny<CancellationToken>()),
            times);

    private static (ClinicHub Hub, Mock<IGroupManager> Groups, Mock<IClientProxy> Proxy, User User) Build(
        string connectionId, StaffPresence presence, User? existing = null)
    {
        var user = existing ?? User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var groups = new Mock<IGroupManager>();
        var proxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubCallerClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);

        var hub = new ClinicHub(users.Object, presence)
        {
            Context = BuildContext(connectionId, user.Id),
            Groups = groups.Object,
            Clients = clients.Object,
        };
        return (hub, groups, proxy, user);
    }

    private static HubCallerContext BuildContext(string connectionId, string? userId)
    {
        var context = new Mock<HubCallerContext>();
        context.Setup(c => c.ConnectionId).Returns(connectionId);
        context.Setup(c => c.ConnectionAborted).Returns(CancellationToken.None);
        context.Setup(c => c.Items).Returns(new Dictionary<object, object?>());

        var identity = userId is null
            ? new ClaimsIdentity() // no claims → not authenticated
            : new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth");
        context.Setup(c => c.User).Returns(new ClaimsPrincipal(identity));

        return context.Object;
    }
}
