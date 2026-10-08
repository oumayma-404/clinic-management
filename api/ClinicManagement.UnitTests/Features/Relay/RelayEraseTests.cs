using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Relay;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>« Effacer la copie » on a retired PC (<c>clinic-pc-copy</c> AC-8.2, AC-8.5): the PC's erase and the cloud's record.</summary>
public sealed class RelayEraseTests : IDisposable
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime T0 = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "relay-erase-" + Guid.NewGuid().ToString("N"));

    private static (ClinicRelay Relay, string Secret) PairedPc()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", T0);
        var secret = relay.Pair("PC-ACCUEIL", "key", null, null, null, T0);
        return (relay, secret);
    }

    private static (ReportRelayErasedCommandHandler Handler, List<AuditEntry> Journal) Cloud(ClinicRelay relay)
    {
        var relays = new Mock<IClinicRelayRepository>();
        relays.Setup(r => r.GetByIdAcrossClinicsAsync(relay.Id, It.IsAny<CancellationToken>())).ReturnsAsync(relay);
        var journal = new List<AuditEntry>();
        var audit = new Mock<IAuditEntryRepository>();
        audit.Setup(a => a.AddRangeAsync(It.IsAny<IReadOnlyCollection<AuditEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<AuditEntry>, CancellationToken>((e, _) => journal.AddRange(e))
            .Returns(Task.CompletedTask);
        return (new ReportRelayErasedCommandHandler(relays.Object, audit.Object, Mock.Of<IUnitOfWork>(),
            NullLogger<ReportRelayErasedCommandHandler>.Instance), journal);
    }

    // AC-8.5: the erase is a journal row, once, and the cloud's card says when.
    [Fact]
    public async Task A_Retired_Pc_Reporting_Its_Erase_Is_Recorded_Once()
    {
        var (pc, secret) = PairedPc();
        pc.Retire(ClinicRelayRetirement.Retired, "local|admin", T0.AddDays(1));
        var (handler, journal) = Cloud(pc);

        Assert.True((await handler.Handle(new ReportRelayErasedCommand(pc.Id, secret), default)).IsSuccess);
        Assert.True((await handler.Handle(new ReportRelayErasedCommand(pc.Id, secret), default)).IsSuccess);

        Assert.NotNull(pc.ErasedAtUtc);
        var row = Assert.Single(journal);
        Assert.Contains(RelayJournal.Erased, row.ChangedFields);
        Assert.StartsWith("Copie effacée du PC le",
            RelayLabels.Sentence(ClinicManagement.Domain.Services.ClinicRelayHealth.Read(pc, T0.AddDays(2)), pc, T0.AddDays(2)));
    }

    [Fact]
    public async Task Only_The_Pc_Itself_And_Only_Once_Retired_May_Report_It()
    {
        var (pc, secret) = PairedPc();
        var (handler, journal) = Cloud(pc);

        var wrongSecret = await handler.Handle(new ReportRelayErasedCommand(pc.Id, "not-the-secret"), default);
        Assert.Equal(RelayRefusals.UnknownRelayCode, wrongSecret.Code);

        // Still the cabinet's spare: an active PC cannot have erased the copy it is keeping.
        var active = await handler.Handle(new ReportRelayErasedCommand(pc.Id, secret), default);
        Assert.Equal(RelayRefusals.NotRetiredCode, active.Code);
        Assert.Null(pc.ErasedAtUtc);
        Assert.Empty(journal);
    }

    // The order is the design: rows in one transaction, the PC marked erased, files after — and nothing marked if the
    // purge fails, so the PC never claims an erase that did not happen.
    [Fact]
    public async Task The_Pc_Erases_Rows_Then_Files_And_Remembers_To_Tell_The_Cloud()
    {
        var order = new List<string>();
        var (eraser, store) = Eraser(order, purgeFails: false);

        var files = await eraser.EraseAsync(ClinicId, T0, default);

        Assert.Equal(new[] { "begin", "purge", "commit", "files" }, order);
        Assert.Equal(7, files);
        var state = store.Load();
        Assert.Equal(T0, state.ErasedAtUtc);
        Assert.False(state.ErasureReported);
    }

    [Fact]
    public async Task A_Failed_Purge_Rolls_Back_And_Claims_Nothing()
    {
        var order = new List<string>();
        var (eraser, store) = Eraser(order, purgeFails: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => eraser.EraseAsync(ClinicId, T0, default));

        Assert.Equal(new[] { "begin", "purge", "rollback" }, order);
        Assert.Null(store.Load().ErasedAtUtc);
    }

    // [AC-7.3] « Effacer la copie » on a PC that holds a cut's work never sent to the cloud: refused before anything moves.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_Copy_Holding_A_Cuts_Work_Is_Never_Erased(bool stillHolding)
    {
        var order = new List<string>();
        var lease = new RelayLease(_dir);
        lease.TakeOver();
        if (!stillHolding)
        {
            lease.End();
        }

        var (eraser, store) = Eraser(order, purgeFails: false, lease);

        Assert.False(eraser.MayErase);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => eraser.EraseAsync(ClinicId, T0, default));
        Assert.Equal(RelayRefusals.CutWorkKept, refused.Message);
        Assert.Empty(order);
        Assert.Null(store.Load().ErasedAtUtc);
    }

    private (RelayLocalEraser Eraser, RelayFollowerStateStore Store) Eraser(
        List<string> order, bool purgeFails, RelayLease? lease = null)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("begin")).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("commit")).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("rollback")).Returns(Task.CompletedTask);

        var purge = new Mock<IClinicPurge>();
        var purgeCall = purge.Setup(p => p.PurgeAsync(ClinicId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("purge"));
        if (purgeFails)
        {
            purgeCall.ThrowsAsync(new InvalidOperationException("a row survived"));
        }
        else
        {
            purgeCall.ReturnsAsync(ClinicPurgeCensus.Empty);
        }

        var files = new Mock<IFileStorage>();
        files.Setup(f => f.DeleteByClinicAsync(ClinicId, It.IsAny<CancellationToken>())).Callback(() => order.Add("files")).ReturnsAsync(7);

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<User>(Array.Empty<User>(), 1, 0, 0));

        var store = new RelayFollowerStateStore(_dir);
        store.Save(new RelayFollowerState { Released = true, ReleasedAtUtc = T0.AddDays(-1) });
        return (new RelayLocalEraser(purge.Object, unitOfWork.Object, files.Object, users.Object, store,
            lease ?? new RelayLease(_dir), NullLogger<RelayLocalEraser>.Instance), store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
