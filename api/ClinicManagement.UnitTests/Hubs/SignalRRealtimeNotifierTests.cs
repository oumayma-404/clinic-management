using ClinicManagement.API.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Hubs;

/// <summary>
/// Verifies the SignalR-backed notifier broadcasts the refetch signal — carrying the changed resource
/// key — to the originating clinic's group only (AC-1/AC-2) and that a broadcast failure is swallowed
/// so it can never fail the committed use case that raised it (AC-5 — real-time is additive).
/// </summary>
public class SignalRRealtimeNotifierTests
{
    private static readonly Guid ClinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static (SignalRRealtimeNotifier notifier, Mock<IHubClients> clients, Mock<IClientProxy> proxy) CreateNotifier()
    {
        var proxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
        var hubContext = new Mock<IHubContext<ClinicHub>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        var notifier = new SignalRRealtimeNotifier(hubContext.Object, NullLogger<SignalRRealtimeNotifier>.Instance);
        return (notifier, clients, proxy);
    }

    // [AC-1][AC-2] Sends the "entityChanged" signal carrying the resource key to the clinic's group only.
    [Fact]
    public async Task NotifyEntityChanged_Sends_Event_With_Resource_To_Its_Clinic_Group()
    {
        var (notifier, clients, proxy) = CreateNotifier();

        await notifier.NotifyEntityChangedAsync(ClinicId, "patients", CancellationToken.None);

        clients.Verify(c => c.Group($"clinic-{ClinicId}"), Times.Once);
        // SendAsync(method, arg1, ct) is the extension over SendCoreAsync(method, [arg1], ct).
        proxy.Verify(
            p => p.SendCoreAsync(
                ClinicHub.EntityChanged,
                It.Is<object?[]>(args => args.Length == 1 && (string)args[0]! == "patients"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // [AC-5] A failed broadcast is logged and swallowed — the caller (a committed command) never sees it.
    [Fact]
    public async Task NotifyEntityChanged_Swallows_Broadcast_Failure()
    {
        var (notifier, _, proxy) = CreateNotifier();
        proxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub unreachable"));

        var exception = await Record.ExceptionAsync(
            () => notifier.NotifyEntityChangedAsync(ClinicId, "appointments", CancellationToken.None));

        Assert.Null(exception);
    }
}
