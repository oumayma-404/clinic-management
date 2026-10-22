using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Relay;
using ClinicManagement.Infrastructure.Security;
using ClinicManagement.Application.Common.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// Prepared sessions (<c>clinic-pc-copy</c> D22): while online, a cabinet app gets a ticket from the cloud and trades it on
/// the PC de secours for a session there, so after a switch nobody signs in again. What is held: only the cloud and that
/// PC can make or read a ticket; it dies with its day and with any change to the account (token version); the PC opens a
/// session only for an active account of its own cabinet, admins only once retired; the key reaches the PC sealed, once.
/// </summary>
public class RelayPreparedSessionTests : IDisposable
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime T0 = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "relay-assertion-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static RelayAssertionClaims Claims(User user, Guid relayId, int? tokenVersion = null, DateTime? at = null) =>
        new(user.Id, user.ClinicId, relayId, tokenVersion ?? user.TokenVersion, at ?? T0, (at ?? T0) + RelayAssertion.Lifetime);

    private static User Doctor() => User.CreateLocalUser(ClinicId, User.RoleDoctor, "dr@cabinet.tn", "hash", "Dr Test");

    // ---- the ticket ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_Ticket_Reads_Back_Under_Its_Key_Until_It_Expires()
    {
        var user = Doctor();
        var relayId = Guid.NewGuid();
        var ticket = RelayAssertion.Issue(Key, Claims(user, relayId));

        var read = RelayAssertion.Read(Key, ticket, T0.AddHours(25));

        Assert.NotNull(read);
        Assert.Equal(user.Id, read!.UserId);
        Assert.Equal(relayId, read.RelayId);
        Assert.Equal(user.TokenVersion, read.TokenVersion);
        Assert.Null(RelayAssertion.Read(Key, ticket, T0.AddHours(26).AddSeconds(1)));
    }

    [Fact]
    public void A_Ticket_Altered_Made_Under_Another_Key_Or_Malformed_Reads_As_Nothing()
    {
        var ticket = RelayAssertion.Issue(Key, Claims(Doctor(), Guid.NewGuid()));
        var parts = ticket.Split('.');
        var other = Enumerable.Repeat((byte)7, 32).ToArray();

        Assert.Null(RelayAssertion.Read(other, ticket, T0));
        Assert.Null(RelayAssertion.Read(Key, $"{parts[0]}.{parts[1]}A.{parts[2]}", T0));
        Assert.Null(RelayAssertion.Read(Key, $"ra2.{parts[1]}.{parts[2]}", T0));
        Assert.Null(RelayAssertion.Read(Key, "garbage", T0));
        Assert.Null(RelayAssertion.Read(Key, null, T0));
    }

    // ---- the PC trades it ---------------------------------------------------------------------------------------

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ISessionFamilyRepository> _families = new();
    private readonly Mock<ILocalAuthService> _auth = new();
    private readonly Mock<IRelayLocalStatus> _local = new();

    private TradeRelayAssertionCommandHandler Trade(User user, Guid relayId)
    {
        var localKey = new Mock<IRelayLocalAssertionKey>();
        localKey.Setup(k => k.Current()).Returns(new RelayLocalAssertionKey(relayId, ClinicId, Key));
        _users.Setup(u => u.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _auth.Setup(a => a.GenerateToken(It.IsAny<User>(), It.IsAny<Guid?>()))
            .Returns(new LocalAuthToken("pc-access", DateTime.UtcNow.AddMinutes(30)));
        _auth.Setup(a => a.GenerateRefreshToken(It.IsAny<User>(), It.IsAny<Guid?>(), true))
            .Returns(new LocalAuthToken("pc-refresh", DateTime.UtcNow.AddDays(30)));
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new TradeRelayAssertionCommandHandler(localKey.Object, _local.Object, _users.Object, _families.Object,
            _auth.Object, unitOfWork.Object, NullLogger<TradeRelayAssertionCommandHandler>.Instance);
    }

    [Fact]
    public async Task A_Valid_Ticket_Opens_A_Session_On_The_Pc_And_Changes_No_Cabinet_Row()
    {
        var user = Doctor();
        var relayId = Guid.NewGuid();
        var ticket = RelayAssertion.Issue(Key, Claims(user, relayId, at: DateTime.UtcNow));

        var result = await Trade(user, relayId).Handle(new TradeRelayAssertionCommand(ticket), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("pc-access", result.Value!.AccessToken);
        Assert.Equal("pc-refresh", result.Value.RefreshToken);
        _families.Verify(f => f.AddAsync(It.Is<SessionFamily>(s => s.UserId == user.Id), It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(u => u.Update(It.IsAny<User>()), Times.Never);
    }

    public static TheoryData<string> Refusals => new() { "other-pc", "token-version", "disabled", "retired-non-admin", "forged" };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Every_Refusal_Is_The_Same_Sentence_And_Opens_Nothing(string why)
    {
        var user = Doctor();
        var relayId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var ticket = why switch
        {
            "other-pc" => RelayAssertion.Issue(Key, Claims(user, Guid.NewGuid(), at: now)),
            "token-version" => RelayAssertion.Issue(Key, Claims(user, relayId, user.TokenVersion + 1, now)),
            "forged" => RelayAssertion.Issue(Enumerable.Repeat((byte)9, 32).ToArray(), Claims(user, relayId, at: now)),
            _ => RelayAssertion.Issue(Key, Claims(user, relayId, at: now)),
        };
        if (why == "disabled")
        {
            user.Deactivate();
            ticket = RelayAssertion.Issue(Key, Claims(user, relayId, at: now));
        }

        _local.Setup(l => l.IsRetired).Returns(why == "retired-non-admin");

        var result = await Trade(user, relayId).Handle(new TradeRelayAssertionCommand(ticket), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ClinicAuthRefusals.RelaySessionRefused, result.Code);
        Assert.Equal(ClinicAuthRefusals.MessageFor(ClinicAuthRefusals.RelaySessionRefused), result.Error);
        _families.Verify(f => f.AddAsync(It.IsAny<SessionFamily>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- the cloud issues it --------------------------------------------------------------------------------------

    private static ClinicRelay Paired(string publicKey = "key")
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", T0.AddDays(-1));
        relay.Pair("PC-ACCUEIL", publicKey, null, null, "build-1", T0.AddDays(-1));
        relay.RecordHeartbeat(new RelayHeartbeat(40, 100, true, 0, 0, null, false, "build-1", null, null, null, null), 40, T0);
        return relay;
    }

    private static GetRelayAssertionQueryHandler Issuer(User user, ClinicRelay? relay, IRelayAssertionKeys keys)
    {
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(user.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var relays = new Mock<IClinicRelayRepository>();
        relays.Setup(r => r.GetCurrentForClinicAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(relay);
        return new GetRelayAssertionQueryHandler(context.Object, users.Object, relays.Object, keys);
    }

    [Fact]
    public async Task The_Cloud_Issues_A_Ticket_The_Pc_Can_Read_Once_The_Key_Exists()
    {
        var keys = new RelayAssertionKeys(new EphemeralDataProtectionProvider());
        var user = Doctor();
        var relay = Paired();

        var before = await Issuer(user, relay, keys).Handle(new GetRelayAssertionQuery(), default);
        Assert.Equal(GetRelayAssertionQueryHandler.NotReadyCode, before.Code);

        relay.EnsureAssertionKey(keys.NewProtectedKey);
        var issued = await Issuer(user, relay, keys).Handle(new GetRelayAssertionQuery(), default);

        Assert.True(issued.IsSuccess, issued.Error);
        var read = RelayAssertion.Read(keys.Open(relay.AssertionKeyProtected)!, issued.Value!.Assertion, DateTime.UtcNow);
        Assert.Equal(relay.Id, read!.RelayId);
        Assert.Equal(user.TokenVersion, read.TokenVersion);
    }

    [Fact]
    public async Task No_Pc_No_Ticket()
    {
        var result = await Issuer(Doctor(), null, new RelayAssertionKeys(new EphemeralDataProtectionProvider()))
            .Handle(new GetRelayAssertionQuery(), default);

        Assert.Equal(GetRelayAssertionQueryHandler.NotReadyCode, result.Code);
    }

    // ---- the key's journey ----------------------------------------------------------------------------------------

    private static async Task<RelayHeartbeatAck> BeatAsync(ClinicRelay relay, IRelayAssertionKeys keys, bool hasKey)
    {
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(relay.Subject);
        var relays = new Mock<IClinicRelayRepository>();
        relays.Setup(r => r.GetByIdAcrossClinicsAsync(relay.Id, It.IsAny<CancellationToken>())).ReturnsAsync(relay);
        var rows = new Mock<IClinicRelayRowStore>();
        rows.Setup(r => r.HighWaterAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(40);
        rows.Setup(r => r.FeedEpochAsync(It.IsAny<CancellationToken>())).ReturnsAsync("e1");
        var build = new Mock<IRelayBuildInfo>();
        build.Setup(b => b.Current).Returns("build-1");
        var handler = new RelayHeartbeatCommandHandler(context.Object, relays.Object, new TenantScope(NullLogger<TenantScope>.Instance),
            rows.Object, build.Object, new Mock<IAuditEntryRepository>().Object, new Mock<IUnitOfWork>().Object,
            NullLogger<RelayHeartbeatCommandHandler>.Instance, assertionKeys: keys);
        var result = await handler.Handle(new RelayHeartbeatCommand(new RelayHeartbeatRequest(
            40, 100, true, 0, 0, null, false, "build-1", DateTime.UtcNow, null, null, null, null, HasAssertionKey: hasKey)), default);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [Fact]
    public async Task The_Key_Is_Minted_Once_And_Reaches_Only_That_Pc_Until_It_Says_It_Holds_It()
    {
        var keys = new RelayAssertionKeys(new EphemeralDataProtectionProvider());
        var (publicKey, privateKey) = RelaySecretEnvelope.NewKeyPair();
        var relay = Paired(publicKey);

        var first = await BeatAsync(relay, keys, hasKey: false);
        var minted = relay.AssertionKeyProtected;
        var second = await BeatAsync(relay, keys, hasKey: true);

        Assert.NotNull(minted);
        Assert.Equal(minted, relay.AssertionKeyProtected);
        Assert.Null(second.AssertionKey);
        var opened = RelaySecretEnvelope.Open(first.AssertionKey, privateKey);
        Assert.Equal(keys.Open(minted), Convert.FromBase64String(opened!));
        Assert.Null(RelaySecretEnvelope.Open(first.AssertionKey, RelaySecretEnvelope.NewKeyPair().PrivateKey));
    }

    [Fact]
    public void The_Pc_Keeps_Its_Key_Under_Its_Own_Ring_And_Forgets_It_At_A_New_Pairing()
    {
        var store = new RelayAssertionKeyStore(new EphemeralDataProtectionProvider(), _dir);
        Directory.CreateDirectory(_dir);
        Assert.False(store.Exists);

        store.Save(Key);
        Assert.Equal(Key, store.TryLoad());
        Assert.NotEqual(Convert.ToBase64String(Key), File.ReadAllText(Path.Combine(_dir, RelayAssertionKeyStore.FileName)));

        store.Delete();
        Assert.Null(store.TryLoad());
    }
}
