using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// « À reprendre » (<c>clinic-pc-copy</c> US-7, AC-7.3 → AC-7.5) and the French line each listed record reads as
/// (AC-5.6, AC-7.4): what an overruled PC held becomes one line per record, never applied, once per cut.
/// </summary>
public class RelayReEnterTests
{
    private const string Build = "20261008_Return+1.0.0+aaaaaaaaaaaa";
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly IReadOnlyDictionary<Guid, string> Names = new Dictionary<Guid, string> { [PatientId] = "Amel Trabelsi" };

    // ---- the French line ------------------------------------------------------------------------------------------

    [Fact]
    public void Each_Record_Reads_As_What_It_Is_Never_As_Json()
    {
        Assert.Equal("Amel Trabelsi", RelayRecordSummary.Describe("Patient", "{\"FirstName\":\"Amel\",\"LastName\":\"Trabelsi\"}", Names));
        Assert.Equal("Fournitures — 22,000 DT — 08/10/2026",
            RelayRecordSummary.Describe("Expense", "{\"Description\":\"Fournitures\",\"Amount\":22,\"ExpenseDate\":\"2026-10-08T10:00:00+00:00\"}", Names));
        Assert.Equal("Note d'honoraires 2026-0793 — 50,000 DT — Amel Trabelsi",
            RelayRecordSummary.Describe("Invoice", $"{{\"Number\":\"2026-0793\",\"TotalTtc\":50,\"PatientId\":\"{PatientId}\"}}", Names));
        Assert.Equal("Rendez-vous du 11/10/2026 à 14:00 — Amel Trabelsi",
            RelayRecordSummary.Describe("Appointment", $"{{\"AppointmentDateTime\":\"2026-10-11T13:00:00+00:00\",\"PatientId\":\"{PatientId}\"}}", Names));
        Assert.Null(RelayRecordSummary.Describe("Patient", null, Names));
        Assert.Null(RelayRecordSummary.Describe("Patient", "not json", Names));
    }

    // [AC-7.5] A numbered document printed on an overruled PC is flagged — only on « À reprendre », only with a number.
    [Fact]
    public void A_Document_Numbered_On_An_Overruled_Pc_Is_Flagged_To_Be_Redone()
    {
        Assert.Equal("Document n° 2026-0042 remis au patient : ce numéro n'est pas valable, à refaire.",
            RelayRecordSummary.Warning(RelayReviewKind.ToReEnter, "Invoice", "{\"Number\":\"2026-0042\"}"));
        Assert.Null(RelayRecordSummary.Warning(RelayReviewKind.BothChanged, "Invoice", "{\"Number\":\"2026-0042\"}"));
        Assert.Null(RelayRecordSummary.Warning(RelayReviewKind.ToReEnter, "Invoice", "{\"Number\":null}"));
        Assert.Null(RelayRecordSummary.Warning(RelayReviewKind.ToReEnter, "Expense", "{\"Number\":\"x\"}"));
    }

    // ---- the lines -----------------------------------------------------------------------------------------------

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static RelayHandbackRequest Cut(params RelayRow[] rows) =>
        new(Guid.NewGuid(), 40, DateTime.UtcNow.AddHours(-2), Array.Empty<RelayHandbackChange>(), rows,
            new[] { new RelayHandbackJournalEntry("local|desk", "desk@cabinet.tn", "Invoice", "n1", (int)AuditAction.Insert, null, DateTime.UtcNow.AddHours(-1)) },
            Array.Empty<RelaySignInTrace>(), Array.Empty<RelayRecoveryCodeUse>());

    // One line per record: a note, not its lines — accounts and delivery state left out, a deletion kept.
    [Fact]
    public void One_Line_Per_Record_Not_Per_Row()
    {
        var cut = Cut(
            new RelayRow("Invoice", "n1", Json("{}")),
            new RelayRow("InvoiceLine", "l1", Json("{}")),
            new RelayRow("User", "local|u1", Json("{}")),
            new RelayRow("Notification", "r1", Json("{}")),
            new RelayRow("Expense", "e1", null));
        IEnumerable<RelayRowKey> Refs(string table, JsonElement _) =>
            table == "InvoiceLine" ? new[] { new RelayRowKey("Invoice", "n1") } : Array.Empty<RelayRowKey>();

        var lines = ListOverruledCutCommandHandler.Lines(cut, Refs);

        Assert.Equal(new[] { ("Expense", "e1"), ("Invoice", "n1") }, lines.Select(l => (l.Key.Table, l.Key.Key)));
        Assert.Null(lines[0].Json);
    }

    private static ClinicRelay Relay(bool reclaimed)
    {
        var now = DateTime.UtcNow;
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC", "local|admin", now.AddDays(-1));
        relay.Pair("PC", "key", null, null, Build, now.AddDays(-1));
        if (reclaimed)
        {
            relay.Reclaim("local|admin", now.AddMinutes(-30));
        }

        return relay;
    }

    private static async Task<(ClinicManagement.Application.Common.Models.Result<RelayHandbackResultDto> Result, List<RelayReviewItem> Added)> ListAsync(
        ClinicRelay relay, RelayHandbackRequest cut, List<RelayReviewItem>? existing = null)
    {
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(relay.Subject);
        var relays = new Mock<IClinicRelayRepository>();
        relays.Setup(r => r.GetByIdAcrossClinicsAsync(relay.Id, It.IsAny<CancellationToken>())).ReturnsAsync(relay);
        var build = new Mock<IRelayBuildInfo>();
        build.Setup(b => b.Current).Returns(Build);
        var store = new Mock<IRelayHandbackStore>();
        store.Setup(s => s.References(It.IsAny<string>(), It.IsAny<JsonElement>())).Returns(Array.Empty<RelayRowKey>());
        store.Setup(s => s.CurrentRowsAsync(ClinicId, It.IsAny<IReadOnlyCollection<RelayRowKey>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<RelayRowKey, string>());
        var added = new List<RelayReviewItem>();
        var reviews = new Mock<IRelayReviewItemRepository>();
        reviews.Setup(r => r.GetForCutAsync(relay.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing ?? new List<RelayReviewItem>());
        reviews.Setup(r => r.AddRangeAsync(It.IsAny<IReadOnlyCollection<RelayReviewItem>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<RelayReviewItem>, CancellationToken>((i, _) => added.AddRange(i))
            .Returns(Task.CompletedTask);

        var handler = new ListOverruledCutCommandHandler(context.Object, relays.Object, new TenantScope(NullLogger<TenantScope>.Instance),
            build.Object, store.Object, reviews.Object, new Mock<IAuditEntryRepository>().Object, new Mock<IUnitOfWork>().Object,
            NullLogger<ListOverruledCutCommandHandler>.Instance);
        return (await handler.Handle(new ListOverruledCutCommand(cut, Build), CancellationToken.None), added);
    }

    // [AC-7.3] Every record is listed « À reprendre », with who entered it on the PC — and nothing is applied.
    [Fact]
    public async Task An_Overruled_Cut_Is_Listed_With_Who_Entered_Each_Record()
    {
        var (result, added) = await ListAsync(Relay(reclaimed: true), Cut(new RelayRow("Invoice", "n1", Json("{\"Number\":\"2026-0042\"}"))));

        Assert.True(result.IsSuccess, result.Error);
        var item = Assert.Single(added);
        Assert.Equal(RelayReviewKind.ToReEnter, item.Kind);
        Assert.Equal("desk@cabinet.tn", item.CabinetChangedBy);
        Assert.Contains("2026-0042", item.CabinetVersion);
    }

    [Fact]
    public async Task A_Cut_Already_Listed_Is_Not_Listed_Twice_And_A_Cut_Nobody_Overruled_Is_Refused()
    {
        var relay = Relay(reclaimed: true);
        var cut = Cut(new RelayRow("Invoice", "n1", Json("{}")));
        var existing = new List<RelayReviewItem>
        {
            new(ClinicId, relay.Id, cut.CutSinceUtc!.Value, RelayReviewKind.ToReEnter, "Invoice", "n1", null, "{}", null, null, null, DateTime.UtcNow),
        };

        var (again, added) = await ListAsync(relay, cut, existing);
        Assert.True(again.Value!.AlreadyApplied);
        Assert.Empty(added);

        var (refused, _) = await ListAsync(Relay(reclaimed: false), cut);
        Assert.Equal(RelayRefusals.NotOverruledCode, refused.Code);
    }

    // [AC-7.4] What an overruled PC held stays on the bell until every record is « Repris ».
    [Fact]
    public void Records_To_Re_Enter_Stay_On_The_Admins_Bell()
    {
        var row = Assert.Single(RelayAlertRules.Wanted(Relay(reclaimed: false), null, Array.Empty<RelayAlert>(), DateTime.UtcNow,
            pendingReEntries: 2), r => r.Alert == RelayAlert.ToReEnter);

        Assert.Equal("2 enregistrements faits sur le PC de secours pendant la coupure sont à reprendre dans le cloud.", row.Message);
    }
}
