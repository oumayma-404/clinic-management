using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// « Reprendre la main » (<c>clinic-pc-copy</c> US-7, D19, AC-7.1, AC-7.2, AC-8.6) and the silent PC's bell row
/// (AC-6.4). The reclaim is the one deliberate exception to « never both writable »: the admin is told, in AC-7.1's
/// words, that what the PC recorded will not come back by itself — so what is held here is that it frees the cloud,
/// that the PC is overruled when it reconnects (and stops, keeping the cut's work), and that a later cut still works.
/// </summary>
public class RelayReclaimTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime T0 = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

    private static RelayHeartbeat Beat(bool holding = false, DateTime? since = null, long underAck = 0, bool stopped = false) =>
        new(10, 100, true, 0, 0, null, false, "build-1", null, null, null, null, null, stopped,
            Holding: holding, HoldingSinceUtc: since, HoldingUnderAckSeq: underAck);

    /// <summary>A ready PC armed by an ack sent at T0 and confirmed, then silent.</summary>
    private static ClinicRelay ArmedThenSilent()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", T0.AddDays(-1));
        relay.Pair("PC-ACCUEIL", "key", null, null, "build-1", T0.AddDays(-1));
        relay.RecordHeartbeat(Beat(), 10, T0);
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, T0), armed: true);
        return relay;
    }

    // ---- the domain ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_Reclaim_Frees_A_Cloud_Locked_By_A_Silent_Pc()
    {
        var relay = ArmedThenSilent();
        var now = T0.AddMinutes(10);
        Assert.True(ClinicWriteLease.IsCloudFenced(relay, now));

        relay.Reclaim("local|admin", now);

        Assert.False(ClinicWriteLease.IsCloudFenced(relay, now));
        Assert.False(ClinicWriteLease.IsCloudFenced(relay, now.AddHours(5)));
        Assert.Null(ClinicWriteLease.LockedSinceUtc(relay, now));
    }

    // The PC's word that it holds the saves is set aside too: that is the whole point of « Reprendre la main ».
    [Fact]
    public void A_Reclaim_Frees_A_Cloud_Whose_Pc_Said_It_Holds_The_Saves()
    {
        var relay = ArmedThenSilent();
        relay.RecordHeartbeat(Beat(holding: true, since: T0.AddMinutes(2), underAck: relay.LastAckSeq), 10, T0.AddMinutes(3));
        Assert.True(ClinicWriteLease.IsCloudFenced(relay, T0.AddMinutes(4)));

        relay.Reclaim("local|admin", T0.AddMinutes(5));

        Assert.Null(relay.PcHoldingSinceUtc);
        Assert.False(ClinicWriteLease.IsCloudFenced(relay, T0.AddMinutes(6)));
    }

    // [D19] The PC comes back still holding a takeover made under an ack sent before the reclaim: overruled. Its word
    // is not recorded (the cloud stays free) and the cloud remembers why its copy stopped.
    [Fact]
    public void A_Takeover_Made_Before_The_Reclaim_Is_Overruled_When_The_Pc_Reconnects()
    {
        var relay = ArmedThenSilent();
        var underAck = relay.LastAckSeq;
        relay.Reclaim("local|admin", T0.AddMinutes(10));

        var beat = Beat(holding: true, since: T0.AddMinutes(2), underAck: underAck);
        Assert.True(relay.IsOverruledHolding(beat));
        relay.RecordHeartbeat(beat, 10, T0.AddMinutes(30));

        Assert.Null(relay.PcHoldingSinceUtc);
        Assert.Equal(T0.AddMinutes(30), relay.CutOverruledAtUtc);
        Assert.False(ClinicWriteLease.IsCloudFenced(relay, T0.AddMinutes(31)));
    }

    // A reclaim is not a retirement: the PC that comes back healthy is armed again, and a LATER cut works as before.
    [Fact]
    public void After_A_Reclaim_A_Later_Cut_Locks_The_Cloud_And_A_Later_Takeover_Holds()
    {
        var relay = ArmedThenSilent();
        relay.Reclaim("local|admin", T0.AddMinutes(10));

        var back = T0.AddMinutes(20);
        relay.RecordHeartbeat(Beat(), 10, back);
        var newAck = relay.IssueAck(armed: true, back);
        relay.RecordAckConfirmation(newAck, armed: true);

        Assert.False(ClinicWriteLease.IsCloudFenced(relay, back.AddSeconds(30)));
        Assert.True(ClinicWriteLease.IsCloudFenced(relay, back.AddSeconds(61)));

        var later = Beat(holding: true, since: back.AddMinutes(2), underAck: newAck);
        Assert.False(relay.IsOverruledHolding(later));
        relay.RecordHeartbeat(later, 10, back.AddMinutes(5));
        Assert.NotNull(relay.PcHoldingSinceUtc);
    }

    [Fact]
    public void The_Lock_Starts_Sixty_Seconds_After_The_Confirmed_Ack_Or_At_The_Pcs_Own_Takeover()
    {
        var silent = ArmedThenSilent();
        Assert.Equal(T0 + ClinicWriteLease.CloudFencesAfter, ClinicWriteLease.LockedSinceUtc(silent, T0.AddMinutes(5)));
        Assert.Null(ClinicWriteLease.LockedSinceUtc(silent, T0.AddSeconds(30)));

        var holding = ArmedThenSilent();
        holding.RecordHeartbeat(Beat(holding: true, since: T0.AddMinutes(2), underAck: holding.LastAckSeq), 10, T0.AddMinutes(3));
        Assert.Equal(T0.AddMinutes(2), ClinicWriteLease.LockedSinceUtc(holding, T0.AddMinutes(4)));
    }

    // ---- the command (AC-7.1, AC-7.2) -------------------------------------------------------------------------------

    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRelayRepository> _relays = new();
    private readonly Mock<IAuditEntryRepository> _audit = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly List<AuditEntry> _journal = new();

    private ReclaimRelayCommandHandler Handler(User caller, ClinicRelay? relay)
    {
        _context.Setup(c => c.GetUserId()).Returns(caller.Id);
        _users.Setup(u => u.GetByAuth0SubAsync(caller.Id, It.IsAny<CancellationToken>())).ReturnsAsync(caller);
        _relays.Setup(r => r.GetCurrentForClinicAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(relay);
        _audit.Setup(a => a.AddRangeAsync(It.IsAny<IReadOnlyCollection<AuditEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<AuditEntry>, CancellationToken>((e, _) => _journal.AddRange(e))
            .Returns(Task.CompletedTask);
        var actor = new Mock<IAuditActorProvider>();
        actor.SetupGet(a => a.Current).Returns(new AuditActor(caller.Id, caller.Email));
        return new ReclaimRelayCommandHandler(_context.Object, _users.Object, _relays.Object, _audit.Object, actor.Object,
            _unitOfWork.Object, NullLogger<ReclaimRelayCommandHandler>.Instance);
    }

    private static User Admin() => User.CreateLocalUser(ClinicId, User.RoleAdmin, "admin@cabinet.tn", "hash", "Salma B.");

    /// <summary>A PC armed by an ack sent ten minutes ago and silent since — the cloud is locked now.</summary>
    private static ClinicRelay LockedNow()
    {
        var sent = DateTime.UtcNow.AddMinutes(-10);
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", sent.AddDays(-1));
        relay.Pair("PC-ACCUEIL", "key", null, null, "build-1", sent.AddDays(-1));
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, sent), armed: true);
        return relay;
    }

    [Fact]
    public async Task An_Admin_Takes_The_Cloud_Back_And_The_Journal_Names_Them()
    {
        var admin = Admin();
        var relay = LockedNow();

        var result = await Handler(admin, relay).Handle(new ReclaimRelayCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value!.CloudLocked);
        Assert.Equal(admin.Id, relay.ReclaimedByUserId);
        var row = Assert.Single(_journal);
        Assert.Equal(admin.Id, row.UserId);
        Assert.StartsWith(RelayJournal.Reclaimed, row.ChangedFields);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Pressed on a cabinet whose cloud is not locked, it would only disarm a PC that was ready to help.
    [Fact]
    public async Task Nothing_Is_Taken_Back_From_A_Cloud_That_Is_Not_Locked()
    {
        var (healthy, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", DateTime.UtcNow.AddDays(-1));

        var result = await Handler(Admin(), healthy).Handle(new ReclaimRelayCommand(), CancellationToken.None);
        var none = await Handler(Admin(), null).Handle(new ReclaimRelayCommand(), CancellationToken.None);

        Assert.Equal(RelayRefusals.NotHoldingCode, result.Code);
        Assert.Equal(RelayRefusals.NotHoldingCode, none.Code);
        Assert.Null(healthy.ReclaimedAtUtc);
        Assert.Empty(_journal);
    }

    [Fact]
    public async Task A_Doctor_Cannot_Take_The_Cloud_Back()
    {
        var doctor = User.CreateLocalUser(ClinicId, User.RoleDoctor, "dr@cabinet.tn", "hash", "Dr X");
        var relay = LockedNow();

        var result = await Handler(doctor, relay).Handle(new ReclaimRelayCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(relay.ReclaimedAtUtc);
    }

    // ---- what the card and the bell say ----------------------------------------------------------------------------

    // AC-7.1 / AC-8.6: the warning names the PC and the moment, and it is on the status the card reads while locked.
    [Fact]
    public void A_Locked_Cabinets_Status_Carries_The_Lock_And_AC_7_1s_Warning()
    {
        var relay = ArmedThenSilent();
        var now = T0.AddMinutes(10);

        var status = GetRelayStatusQueryHandler.ToDto(relay, now);

        Assert.True(status.CloudLocked);
        Assert.False(status.PcHolding);
        Assert.Equal(T0 + ClinicWriteLease.CloudFencesAfter, status.LockedSinceUtc);
        Assert.Equal(
            "Ce que le cabinet a enregistré sur PC-ACCUEIL depuis 10:00 ne partira pas dans le cloud : il faudra le saisir "
            + "à nouveau, et les numéros de notes émis sur ce PC seront en double. Appelez le cabinet avant de continuer.",
            status.ReclaimWarning);

        Assert.Equal("Le PC de secours ne répond plus depuis 10:00 : le cloud refuse les enregistrements du cabinet.",
            status.LockSentence);

        var free = GetRelayStatusQueryHandler.ToDto(relay, T0.AddSeconds(30));
        Assert.False(free.CloudLocked);
        Assert.Null(free.ReclaimWarning);
    }

    // AC-6.4: told at once and at any hour, in place of « éteint »; never while the PC holds the saves (that is a cut).
    [Fact]
    public void A_Silent_Armed_Pc_Raises_Its_Own_Bell_Row_At_Once()
    {
        var relay = ArmedThenSilent();
        var now = T0.AddMinutes(2);

        var rows = RelayAlertRules.Wanted(relay, null, Array.Empty<RelayAlert>(), now);

        var row = Assert.Single(rows);
        Assert.Equal(RelayAlert.Silent, row.Alert);
        Assert.StartsWith("Le PC de secours ne répond plus depuis 10:00", row.Message);
    }

    [Fact]
    public void No_Silent_Row_While_The_Pc_Holds_The_Saves_Or_Once_Reclaimed()
    {
        var holding = ArmedThenSilent();
        holding.RecordHeartbeat(Beat(holding: true, since: T0.AddMinutes(2), underAck: holding.LastAckSeq), 10, T0.AddMinutes(3));
        var reclaimed = ArmedThenSilent();
        reclaimed.Reclaim("local|admin", T0.AddMinutes(5));

        Assert.DoesNotContain(RelayAlertRules.Wanted(holding, null, Array.Empty<RelayAlert>(), T0.AddMinutes(4)),
            r => r.Alert == RelayAlert.Silent);
        Assert.DoesNotContain(RelayAlertRules.Wanted(reclaimed, null, Array.Empty<RelayAlert>(), T0.AddMinutes(6)),
            r => r.Alert == RelayAlert.Silent);
    }

    // The overruled PC's copy stops — and says why, instead of « le cloud est revenu à un état antérieur ».
    [Fact]
    public void An_Overruled_Pcs_Stopped_Copy_Names_The_Reclaim()
    {
        var relay = ArmedThenSilent();
        var underAck = relay.LastAckSeq;
        relay.Reclaim("local|admin", T0.AddMinutes(10));
        relay.RecordHeartbeat(Beat(holding: true, underAck: underAck), 10, T0.AddMinutes(30));
        relay.RecordHeartbeat(Beat(stopped: true), 10, T0.AddMinutes(31));
        var now = T0.AddMinutes(32);

        var reading = ClinicRelayHealth.Read(relay, now);
        Assert.Equal(ClinicRelayState.Stopped, reading.State);
        Assert.Contains("le cloud a repris la main pendant la coupure", RelayLabels.Sentence(reading, relay, now));
        Assert.Contains(RelayAlertRules.Wanted(relay, null, Array.Empty<RelayAlert>(), now),
            r => r.Alert == RelayAlert.Stopped && r.Message.StartsWith("Le cloud a repris la main", StringComparison.Ordinal));
    }
}
