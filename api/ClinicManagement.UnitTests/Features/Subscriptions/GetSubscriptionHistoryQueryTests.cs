using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Subscriptions.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Subscriptions;

/// <summary>
/// `GET /api/subscription/history` — what the cabinet has paid (`clinic-subscription` Part C, AC-2.3).
///
/// <para><b>The load-bearing case is <see cref="Page_Two_Continues_Page_Ones_Periods_Rather_Than_Restarting_Them"/>.</b>
/// Each entry's « période couverte » is a function of every non-cancelled entry recorded before it, so a SQL
/// <c>OFFSET</c> would hand the fold a window and page 2 would restart its dates from that window's first row — the
/// figures would look entirely plausible and describe periods the cabinet was never entitled to. Nothing else in
/// this project can see that: the shape is right, the ordering is right, only the arithmetic is quietly local.</para>
///
/// <para>Every date here is a fixed literal and there is no <c>DateTime.UtcNow</c> in the file: unlike
/// <see cref="GetSubscriptionQueryTests"/>, nothing in this read depends on today, so nothing here may either.</para>
/// </summary>
public class GetSubscriptionHistoryQueryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private sealed class Harness
    {
        public Mock<IClinicSubscriptionRepository> Subscriptions { get; } = new();

        public GetSubscriptionHistoryQueryHandler Handler()
        {
            var resolver = new Mock<ICurrentClinicResolver>();
            resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Success(ClinicId));

            return new GetSubscriptionHistoryQueryHandler(
                Subscriptions.Object, resolver.Object,
                NullLogger<GetSubscriptionHistoryQueryHandler>.Instance);
        }

        public void With(params SubscriptionPeriod[] entries) =>
            Subscriptions.Setup(s => s.GetEntriesAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(entries);
    }

    /// <summary>A dated entry. <paramref name="recordedAt"/> is the fold's order, so it is spelled out per entry.</summary>
    private static SubscriptionPeriod Paid(DateTime day, int months, decimal amount, string reference) =>
        SubscriptionPeriod.Create(
            ClinicId, SubscriptionPeriodKind.Paid, day, day.AddHours(9),
            durationMonths: months, amount: amount, method: SubscriptionPaymentMethod.Cheque,
            reference: reference, note: "Réglé par chèque.", recordedBy: "job|subscription-grant");

    private static async Task<SubscriptionHistoryPageDto> Read(Harness harness, int? page = null, int? size = null)
    {
        var result = await harness.Handler().Handle(
            new GetSubscriptionHistoryQuery { Page = page, PageSize = size }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    // ---- the derived period ----------------------------------------------------------------------------

    // [AC-2.3][FR-2] The period covered comes out of the same fold that produces the enforced end date: each entry
    // resumes exactly where the previous cover ran out, with no gap and no overlap.
    [Fact]
    public async Task Each_Entry_Resumes_Where_The_Previous_Cover_Ran_Out()
    {
        var harness = new Harness();
        harness.With(
            Paid(Jan1, months: 1, 120.000m, "VIR-1"),
            Paid(Jan1.AddDays(10), months: 1, 120.000m, "VIR-2"));

        var page = await Read(harness);
        var first = page.Items.Single(i => i.Reference == "VIR-1");
        var second = page.Items.Single(i => i.Reference == "VIR-2");

        Assert.Equal(Jan1, first.FromDay);
        Assert.Equal(new DateTime(2026, 1, 31), first.ThroughDay);
        // Not its own recorded day (10 January): the cabinet was still covered, so the second month starts where the
        // first ended. Anchoring on the recorded day instead would silently give the cabinet 21 days less.
        Assert.Equal(new DateTime(2026, 2, 1), second.FromDay);
        Assert.Equal(new DateTime(2026, 2, 28), second.ThroughDay);
    }

    // [AC-2.3] THE case this read is shaped around. Page 2's entry must carry the dates the whole-ledger fold gives
    // it — identical to an unpaged read — not dates recomputed from page 2's own first row.
    [Fact]
    public async Task Page_Two_Continues_Page_Ones_Periods_Rather_Than_Restarting_Them()
    {
        var harness = new Harness();
        harness.With(
            Paid(Jan1, months: 1, 120.000m, "VIR-1"),
            Paid(Jan1.AddMonths(1), months: 1, 120.000m, "VIR-2"),
            Paid(Jan1.AddMonths(2), months: 1, 120.000m, "VIR-3"));

        // Newest first, so page 1 holds VIR-3 + VIR-2 and page 2 holds the oldest.
        var pageOne = await Read(harness, page: 1, size: 2);
        var pageTwo = await Read(harness, page: 2, size: 2);
        var unpaged = await Read(harness);

        Assert.Equal(new[] { "VIR-3", "VIR-2" }, pageOne.Items.Select(i => i.Reference));
        Assert.Equal(new[] { "VIR-1" }, pageTwo.Items.Select(i => i.Reference));

        var oldestPaged = pageTwo.Items.Single();
        var oldestUnpaged = unpaged.Items.Single(i => i.Reference == "VIR-1");
        Assert.Equal(oldestUnpaged.FromDay, oldestPaged.FromDay);
        Assert.Equal(oldestUnpaged.ThroughDay, oldestPaged.ThroughDay);
        Assert.Equal(Jan1, oldestPaged.FromDay);
    }

    // [AC-5.5][EC-4] A cancelled entry is kept, marked, and covers **nothing** — both span ends null, so no screen
    // can render it as a period the cabinet was entitled to. Its motif travels, because the end date may have moved
    // into the past because of it.
    [Fact]
    public async Task A_Cancelled_Entry_Is_Kept_Covers_Nothing_And_Carries_Its_Motif()
    {
        var cancelled = Paid(Jan1, months: 12, 1200.000m, "VIR-ERREUR");
        cancelled.Cancel("Montant saisi deux fois.", "job|subscription-cancel", Jan1.AddDays(3));

        var harness = new Harness();
        harness.With(cancelled, Paid(Jan1.AddDays(5), months: 1, 120.000m, "VIR-OK"));

        var page = await Read(harness);
        var voided = page.Items.Single(i => i.Reference == "VIR-ERREUR");

        Assert.True(voided.IsCancelled);
        Assert.Null(voided.FromDay);
        Assert.Null(voided.ThroughDay);
        Assert.Equal("Montant saisi deux fois.", voided.CancelReason);
        Assert.Equal(Jan1.AddDays(3), voided.CancelledAt);

        // And the entry beside it anchors on its own recorded day, because the cancelled one covered nothing.
        Assert.Equal(Jan1.AddDays(5), page.Items.Single(i => i.Reference == "VIR-OK").FromDay);
    }

    // [AC-2.5] An open-ended entry has a start and no end, and the null must be readable as « sans échéance » rather
    // than confused with a cancelled entry's — which is why `isCancelled` and not the nulls tells them apart.
    [Fact]
    public async Task An_Open_Ended_Entry_Has_A_Start_And_No_End()
    {
        var harness = new Harness();
        harness.With(SubscriptionPeriod.OpenEnded(
            ClinicId, SubscriptionPeriodKind.Grandfathered, Jan1, Jan1.AddHours(9),
            note: "Cabinet existant à la mise en service."));

        var entry = (await Read(harness)).Items.Single();

        Assert.False(entry.IsCancelled);
        Assert.Equal(Jan1, entry.FromDay);
        Assert.Null(entry.ThroughDay);
        Assert.Equal("Antériorité", entry.KindLabel);
    }

    // ---- order, labels and the wire ---------------------------------------------------------------------

    // [AC-2.3] Newest first, like the audit ledger and the bell: the entry an owner opens this screen to check is the
    // one they have just paid.
    [Fact]
    public async Task The_History_Reads_Newest_First()
    {
        var harness = new Harness();
        harness.With(
            Paid(Jan1, months: 1, 120.000m, "VIR-1"),
            Paid(Jan1.AddMonths(1), months: 1, 120.000m, "VIR-2"),
            Paid(Jan1.AddMonths(2), months: 1, 120.000m, "VIR-3"));

        Assert.Equal(new[] { "VIR-3", "VIR-2", "VIR-1" }, (await Read(harness)).Items.Select(i => i.Reference));
    }

    // [AC-2.3] The wire carries the stable key **and** its French name for both closed sets, so a caller filters on
    // the key while a reader sees « Chèque » — never a raw `Cheque` in a French screen.
    [Fact]
    public async Task Every_Row_Carries_Both_The_Key_And_Its_French_Label()
    {
        var harness = new Harness();
        harness.With(Paid(Jan1, months: 12, 1200.000m, "VIR-1"));

        var entry = (await Read(harness)).Items.Single();

        Assert.Equal("Paid", entry.Kind);
        Assert.Equal("Paiement", entry.KindLabel);
        Assert.Equal("Cheque", entry.Method);
        Assert.Equal("Chèque", entry.MethodLabel);
        Assert.Equal(1200.000m, entry.Amount);
        Assert.Equal("VIR-1", entry.Reference);
    }

    // The vendor's own annotations stay on the vendor's console. `--note` is commentary ABOUT this customer (« geste
    // commercial », « pilote ») and `RecordedBy` publishes our internal command vocabulary; neither is rendered by
    // either tree of the history table, so putting them on the wire was exposure with no product benefit. Asserted
    // over the DTO's shape rather than over a row, because the defect is a property existing at all.
    [Fact]
    public void The_History_Row_Carries_No_Vendor_Internal_Field()
    {
        var exposed = typeof(SubscriptionPeriodDto).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("Note", exposed);
        Assert.DoesNotContain("RecordedBy", exposed);
    }

    // The pager's counts describe the whole filtered set, not the rows on screen — « 1 paiement sur 3 » is what makes
    // the page controls honest, and it is the number `PagedResult` exists to carry.
    [Fact]
    public async Task The_Page_Reports_The_Whole_Ledgers_Total()
    {
        var harness = new Harness();
        harness.With(
            Paid(Jan1, months: 1, 120.000m, "VIR-1"),
            Paid(Jan1.AddMonths(1), months: 1, 120.000m, "VIR-2"),
            Paid(Jan1.AddMonths(2), months: 1, 120.000m, "VIR-3"));

        var page = await Read(harness, page: 2, size: 2);

        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.True(page.HasPreviousPage);
        Assert.False(page.HasNextPage);
    }

    // A cabinet whose ledger is somehow empty reads as an empty page, never as a failure: the screen above it still
    // has a state and a date to show, and « l'historique n'a pas pu être chargé » would be a false alarm.
    [Fact]
    public async Task An_Empty_Ledger_Is_An_Empty_Page_Not_A_Failure()
    {
        var harness = new Harness();
        harness.With();

        var page = await Read(harness);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.TotalPages);
    }
}
