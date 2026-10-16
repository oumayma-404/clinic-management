using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Relay;

/// <summary>
/// <c>clinic-pc-copy</c> D16, FR-3, EC-22: a note, devis or avoir number is final on the cloud only once the PC de
/// secours holds it — so a PC taking over seconds later never issues it again. What a save numbers, who must confirm,
/// the channel that carries it, and the refusal when the PC does not answer. The PC's floor is SQL and the rig's.
/// </summary>
public class NumberPromiseTests
{
    private static readonly Guid ClinicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTime T0 = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

    // ---- what a save numbers ----------------------------------------------------------------------------------------

    private static ApplicationDbContext ModelOnly() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=none;Password=none").Options);

    private static Invoice Draft()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, Guid.NewGuid());
        invoice.SetLines(new[] { ("Détartrage", 1, 60m) });
        return invoice;
    }

    [Fact]
    public void A_Note_Issued_In_This_Save_Is_A_Number_To_Promise_And_A_Draft_Is_Not()
    {
        using var db = ModelOnly();
        var issued = Draft();
        issued.Issue("2026-0042");
        db.Add(issued);
        db.Add(Draft());

        var numbers = NumberedDocuments.Collect(db);

        Assert.Equal(new[] { new NumberedDocument(ClinicId, RelayNumberPromise.InvoiceSequence, "2026-0042") }, numbers);
    }

    [Fact]
    public void A_Note_Issued_After_Loading_Is_Promised_And_A_Payment_On_An_Issued_One_Is_Not()
    {
        using var db = ModelOnly();
        var draft = Draft();
        db.Attach(draft);
        draft.Issue("2026-0043");

        var already = Draft();
        already.Issue("2026-0040");
        db.Attach(already);
        already.RecordPayment(60m, PaymentMethod.Cash, T0);

        var numbers = NumberedDocuments.Collect(db);

        Assert.Equal(new[] { "2026-0043" }, numbers.Select(n => n.Number));
    }

    [Fact]
    public void An_Avoir_Raised_In_This_Save_Is_Promised()
    {
        using var db = ModelOnly();
        db.Add(new CreditNote(Guid.NewGuid(), ClinicId, Guid.NewGuid(), "2026-0003", 10m, "Acte non réalisé",
            PaymentMethod.Cash, T0));

        var number = Assert.Single(NumberedDocuments.Collect(db));
        Assert.Equal(RelayNumberPromise.CreditNoteSequence, number.Sequence);
        Assert.Equal("2026-0003", number.Number);
    }

    // A fourth numbered document added later must be promised too — or the PC would issue its numbers again.
    [Fact]
    public void Every_Numbered_Document_In_The_Model_Is_Covered()
    {
        using var db = ModelOnly();
        var numbered = db.Model.GetEntityTypes()
            .Where(t => t.ClrType.GetProperty("Number")?.PropertyType == typeof(string))
            .Select(t => t.ClrType)
            // The promise store itself: it records numbers, it is not a numbered document.
            .Where(t => t != typeof(RelayNumberPromise))
            .ToHashSet();

        Assert.NotEmpty(numbered);
        Assert.Equal(numbered.OrderBy(t => t.Name), NumberedDocuments.Sequences.Keys.OrderBy(t => t.Name));
    }

    // ---- who must confirm --------------------------------------------------------------------------------------------

    private static RelayHeartbeat Beat(bool holding = false, long underAck = 0) =>
        new(10, 100, true, 0, 0, null, false, "build-1", null, "192.168.1.20", null, null, new string('A', 64), false,
            Holding: holding, HoldingSinceUtc: holding ? T0 : null, HoldingUnderAckSeq: underAck);

    private static ClinicRelay Armed()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", T0.AddDays(-1));
        relay.Pair("PC-ACCUEIL", "key", null, null, "build-1", T0.AddDays(-1));
        relay.RecordHeartbeat(Beat(), 10, T0);
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, T0), armed: true);
        return relay;
    }

    // Review finding 1 (2026-10-13): the cloud never waits for its PC before a number is final. It used to whenever the
    // PC's last ack said « armé » — and a laptop asleep holds that ack all night, so every note, devis and avoir on the
    // cloud waited 3 s and failed until morning, with no way out but retiring the PC.
    [Fact]
    public void The_Cloud_Never_Waits_For_Its_Pc_Before_A_Number_Is_Final()
    {
        Assert.False(ClinicWriteLease.PcMustConfirmNumbers(null));
        Assert.False(ClinicWriteLease.PcMustConfirmNumbers(Armed()));

        var pending = Armed();
        pending.IssueAck(armed: true, T0.AddSeconds(5));
        Assert.False(ClinicWriteLease.PcMustConfirmNumbers(pending));

        // The case that blocked billing: armed, and not heard from for fourteen hours (a lid closed overnight).
        var asleep = Armed();
        Assert.False(ClinicWriteLease.PcMustConfirmNumbers(asleep));

        var holding = Armed();
        holding.RecordHeartbeat(Beat(holding: true, underAck: holding.LastAckSeq), 10, T0.AddMinutes(3));
        Assert.False(ClinicWriteLease.PcMustConfirmNumbers(holding));
    }

    [Fact]
    public void No_Promise_Is_Planned_Whatever_The_Pc_Holds()
    {
        var numbers = new[]
        {
            new NumberedDocument(ClinicId, RelayNumberPromise.InvoiceSequence, "2026-0042"),
            new NumberedDocument(ClinicId, RelayNumberPromise.CreditNoteSequence, "2026-0003"),
        };

        Assert.Empty(NumberPromiseCoordinator.Plan(Armed(), numbers, "key-123456"));
        Assert.Empty(NumberPromiseCoordinator.Plan(null, numbers, null));
    }

    // ---- the PC de secours never gives a legal number --------------------------------------------------------------

    [Fact]
    public void A_Note_Issued_On_A_Pc_Holding_A_Cut_Gets_A_Provisional_Number_Of_Its_Year()
    {
        using var db = ModelOnly();
        var invoice = Draft();
        invoice.Issue("2026-0043");
        db.Add(invoice);

        Assert.Equal(1, NumberedDocuments.MakeProvisional(db));

        Assert.Equal($"2026-P{invoice.Id.ToString("N")[..8].ToUpperInvariant()}", invoice.Number);
        Assert.True(ProvisionalNumber.IsProvisional(invoice.Number));
    }

    [Fact]
    public void An_Avoir_Raised_On_A_Pc_Holding_A_Cut_Is_Provisional_Too()
    {
        using var db = ModelOnly();
        var avoir = new CreditNote(Guid.NewGuid(), ClinicId, Guid.NewGuid(), "2026-0003", 10m, "Acte non réalisé",
            PaymentMethod.Cash, T0);
        db.Add(avoir);

        NumberedDocuments.MakeProvisional(db);

        Assert.True(ProvisionalNumber.IsProvisional(avoir.Number));
        Assert.StartsWith("2026-P", avoir.Number);
    }

    // A payment on a note issued earlier in the cut keeps its provisional number — it is not numbered again.
    [Fact]
    public void A_Note_Already_Provisional_Or_Already_Numbered_Is_Left_Alone()
    {
        using var db = ModelOnly();
        var earlier = Draft();
        earlier.Issue("2026-0040");
        db.Attach(earlier);
        earlier.RecordPayment(20m, PaymentMethod.Cash, T0);

        var provisional = Draft();
        provisional.Issue("2026-P0A1B2C3D");
        db.Add(provisional);

        Assert.Equal(0, NumberedDocuments.MakeProvisional(db));
        Assert.Equal("2026-0040", earlier.Number);
        Assert.Equal("2026-P0A1B2C3D", provisional.Number);
    }

    // The next real number is read off the real ones only: a provisional number can never be mistaken for a sequence.
    [Theory]
    [InlineData("2026-0042", false)]
    [InlineData("2026-P0A1B2C3D", true)]
    [InlineData("2026-P12345678", true)]
    [InlineData("2026-12345678", false)]
    [InlineData("P-0042", false)]
    public void A_Provisional_Number_Never_Reads_As_A_Sequence(string number, bool provisional)
    {
        Assert.Equal(provisional, ProvisionalNumber.IsProvisional(number));
        if (provisional)
        {
            // The three GetMaxSequenceForYearAsync parse what follows the last dash as an int: « P… » never is.
            Assert.False(int.TryParse(number[(number.LastIndexOf('-') + 1)..], out _));
        }
    }

    // The return renumbers exactly the documents the PC numbers provisionally — a fourth numbered type must be in both.
    [Fact]
    public void Every_Numbered_Document_Is_Renumbered_At_The_Return_In_Issue_Order()
    {
        Assert.Equal(
            NumberedDocuments.Sequences.Keys.Select(t => t.Name).Order(),
            ClinicRelayRowStore.RenumberedInIssueOrder.Keys.Order());
        foreach (var (table, issuedAt) in ClinicRelayRowStore.RenumberedInIssueOrder)
        {
            var type = NumberedDocuments.Sequences.Keys.Single(t => t.Name == table);
            Assert.NotNull(type.GetProperty(issuedAt));
        }
    }

    // ---- the refusal ---------------------------------------------------------------------------------------------------

    private sealed class FakeBroker : IRelayPromiseBroker
    {
        public bool Keeps { get; init; } = true;
        public List<IReadOnlyList<RelayNumberPromiseDto>> Calls { get; } = new();

        public Task<bool> PromiseAsync(Guid relayId, IReadOnlyList<RelayNumberPromiseDto> promises, TimeSpan timeout, CancellationToken ct)
        {
            Calls.Add(promises);
            return Task.FromResult(Keeps);
        }

        public Task<IReadOnlyList<RelayNumberPromiseDto>> ExchangeAsync(Guid relayId, IReadOnlyCollection<Guid> acks, TimeSpan wait, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static NumberPromiseCoordinator Coordinator(IRelayPromiseBroker broker, DeploymentKind kind = DeploymentKind.HostedMultiTenant) =>
        new(DeploymentProfile.For(kind), broker, null, NullLogger<NumberPromiseCoordinator>.Instance);

    private static PlannedPromise Promise(string number = "2026-0042") =>
        new(Guid.NewGuid(), new RelayNumberPromiseDto(Guid.NewGuid(), ClinicId, RelayNumberPromise.InvoiceSequence, number, null));

    [Fact]
    public async Task A_Number_The_Pc_Kept_Lets_The_Save_Commit()
    {
        var broker = new FakeBroker();
        await Coordinator(broker).ConfirmAsync(new[] { Promise() }, default);
        Assert.Single(broker.Calls);
    }

    // FR-3: « Le PC de secours ne répond plus — réessayez dans un instant. », the save undone, the form open.
    [Fact]
    public async Task A_Number_The_Pc_Did_Not_Confirm_Undoes_The_Save_With_Fr3s_Sentence()
    {
        var error = await Assert.ThrowsAsync<ClinicFencedException>(() =>
            Coordinator(new FakeBroker { Keeps = false }).ConfirmAsync(new[] { Promise() }, default));

        Assert.Equal(RelayRefusals.UnconfirmedCode, error.Code);
        Assert.Equal(RelayRefusals.Silent, error.Message);
    }

    [Fact]
    public async Task Nothing_To_Promise_Asks_Nothing_And_Only_The_Cloud_Promises()
    {
        var broker = new FakeBroker();
        await Coordinator(broker).ConfirmAsync(Array.Empty<PlannedPromise>(), default);
        Assert.Empty(broker.Calls);

        Assert.True(Coordinator(broker).Promises);
        Assert.False(Coordinator(broker, DeploymentKind.ClinicRelay).Promises);
        Assert.False(Coordinator(broker, DeploymentKind.SelfHostedLan).Promises);
    }

    [Theory]
    [InlineData("2026-0042", 2026, 42)]
    [InlineData("2025-0042", 2026, 0)]
    [InlineData("2026-abc", 2026, 0)]
    [InlineData(null, 2026, 0)]
    public void The_Sequence_Part_Of_A_Number(string? number, int year, int expected)
    {
        Assert.Equal(expected, RelayNumberPromise.SequenceOf(number, year));
    }

    // ---- the channel -----------------------------------------------------------------------------------------------------

    private static RelayNumberPromiseDto Dto(string number = "2026-0042") =>
        new(Guid.NewGuid(), ClinicId, RelayNumberPromise.InvoiceSequence, number, null);

    [Fact]
    public async Task A_Promise_Reaches_The_Waiting_Poll_And_Is_Kept_When_The_Next_Poll_Acknowledges_It()
    {
        var broker = new RelayPromiseBroker();
        var relay = Guid.NewGuid();
        // Generous on the success path: under a full parallel suite the thread pool can stall for seconds.
        var poll = broker.ExchangeAsync(relay, Array.Empty<Guid>(), TimeSpan.FromSeconds(30), default);

        var promise = Dto();
        var kept = broker.PromiseAsync(relay, new[] { promise }, TimeSpan.FromSeconds(30), default);

        var handed = await poll;
        Assert.Equal(promise.Id, Assert.Single(handed).Id);
        Assert.False(kept.IsCompleted);

        _ = broker.ExchangeAsync(relay, new[] { promise.Id }, TimeSpan.FromMilliseconds(50), default);
        Assert.True(await kept);
    }

    [Fact]
    public async Task A_Promise_Made_Before_The_Poll_Is_Handed_At_Once()
    {
        var broker = new RelayPromiseBroker();
        var relay = Guid.NewGuid();
        var promise = Dto();
        var kept = broker.PromiseAsync(relay, new[] { promise }, TimeSpan.FromSeconds(30), default);

        var handed = await broker.ExchangeAsync(relay, Array.Empty<Guid>(), TimeSpan.FromSeconds(30), default);
        Assert.Single(handed);
        await broker.ExchangeAsync(relay, new[] { promise.Id }, TimeSpan.FromMilliseconds(10), default);
        Assert.True(await kept);
    }

    // A silent PC: the promise lapses, and a PC reconnecting later is never handed a number the cloud refused.
    [Fact]
    public async Task An_Unanswered_Promise_Lapses_And_Is_Withdrawn()
    {
        var broker = new RelayPromiseBroker();
        var relay = Guid.NewGuid();

        Assert.False(await broker.PromiseAsync(relay, new[] { Dto() }, TimeSpan.FromMilliseconds(100), default));
        Assert.Empty(await broker.ExchangeAsync(relay, Array.Empty<Guid>(), TimeSpan.FromMilliseconds(50), default));
    }

    [Fact]
    public async Task A_Promise_Handed_But_Never_Acknowledged_Lapses()
    {
        var broker = new RelayPromiseBroker();
        var relay = Guid.NewGuid();
        var kept = broker.PromiseAsync(relay, new[] { Dto() }, TimeSpan.FromMilliseconds(200), default);
        Assert.Single(await broker.ExchangeAsync(relay, Array.Empty<Guid>(), TimeSpan.FromSeconds(1), default));

        Assert.False(await kept);
    }

    [Fact]
    public async Task Another_Pcs_Promise_Is_Never_Handed_And_A_Quiet_Poll_Returns_Empty()
    {
        var broker = new RelayPromiseBroker();
        _ = broker.PromiseAsync(Guid.NewGuid(), new[] { Dto() }, TimeSpan.FromMilliseconds(300), default);

        var started = DateTime.UtcNow;
        Assert.Empty(await broker.ExchangeAsync(Guid.NewGuid(), new[] { Guid.NewGuid() }, TimeSpan.FromMilliseconds(150), default));
        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(100));
    }
}
