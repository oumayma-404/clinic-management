using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Application.Features.Messaging.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Messaging;

/// <summary>
/// US-2's two clinic reads (<c>vendor-whatsapp-messaging-quota</c> AC-2.1–2.7, D-5).
///
/// <para>The whole substance here is the <b>three-way distinction</b> the spec insists on and that a careless
/// implementation collapses: « 0 rappel envoyé » (a measured zero, a fact about the practice) · « non mesuré » (no
/// counting row, a fact about <i>us</i>) · a failed read (a <c>Result.Failure</c>, never a zeroed DTO). Every case
/// below exists to keep two of those three apart.</para>
///
/// <para>⚠️ These fixtures anchor on <c>ClinicClock.CurrentMonthKey()</c> rather than a literal month, which is the
/// opposite of what <c>ClinicClockTests</c> does — and deliberately: the property under test is « the row for the month
/// the clinic is in <i>now</i> », so a fixture pinned to August 2026 would have no matching row at all and the case
/// would cease to exist. What the month key *is* belongs to <c>ClinicClockMonthTests</c>.</para>
/// </summary>
public class ReminderAllowanceQueryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // ---- The current month (AC-2.1, AC-2.4, AC-2.7) -------------------------------------------------

    /// <summary>AC-2.1 — allowance, consumed and remaining for the current Tunisian month.</summary>
    [Fact]
    public async Task The_Current_Month_Reports_Allowance_Consumed_And_Remaining()
    {
        var harness = new Harness(Month(allowance: 200, consumed: 143));

        var dto = await harness.Read();

        Assert.Equal(ClinicClock.CurrentMonthKey(), dto.Month);
        Assert.Equal(200, dto.Allowance);
        Assert.Equal(143, dto.Consumed);
        Assert.Equal(57, dto.Remaining);
        Assert.False(dto.Exhausted);
        Assert.True(dto.Measured);
    }

    /// <summary>
    /// AC-2.4 — a quiet month is a <b>measured zero</b>: three real figures and <c>measured: true</c>. The card reads
    /// « 0 rappel envoyé », which is a statement about the practice.
    /// </summary>
    [Fact]
    public async Task A_Quiet_Month_Is_A_Measured_Zero()
    {
        var harness = new Harness(Month(allowance: 200, consumed: 0));

        var dto = await harness.Read();

        Assert.True(dto.Measured);
        Assert.Equal(0, dto.Consumed);
        Assert.Equal(200, dto.Remaining);
    }

    /// <summary>
    /// AC-2.4 — <b>no counting row is not a zero.</b> The three figures come back <c>null</c> and
    /// <c>measured: false</c>, so the screen cannot render « 0 restant » — a statement about the cabinet where the
    /// truth is a statement about us. <c>exhausted</c> is <b>false</b>, because an unknown is not an exhaustion.
    /// </summary>
    [Fact]
    public async Task No_Counting_Row_Reports_Null_Rather_Than_Zero()
    {
        var harness = new Harness(month: null);

        var dto = await harness.Read();

        Assert.False(dto.Measured);
        Assert.Null(dto.Allowance);
        Assert.Null(dto.Consumed);
        Assert.Null(dto.Remaining);
        Assert.False(dto.Exhausted);
    }

    /// <summary>
    /// AC-7.4 — remaining is floored at 0. A cancelled top-up can leave consumption above the allowance, and
    /// « −50 rappels » is not a quantity anyone can act on; the month reads « épuisé » instead.
    /// </summary>
    [Fact]
    public async Task Remaining_Is_Floored_At_Zero_When_A_Cancellation_Leaves_Consumption_Higher()
    {
        var harness = new Harness(Month(allowance: 200, consumed: 250));

        var dto = await harness.Read();

        Assert.Equal(0, dto.Remaining);
        Assert.True(dto.Exhausted);
        Assert.Equal(250, dto.Consumed); // untouched — the messages were sent and the vendor paid for them
    }

    /// <summary>
    /// AC-2.7 — the renewal date is the first of the next Tunisian month, and the contact route comes from
    /// <b>operator configuration</b>.
    /// </summary>
    [Fact]
    public async Task The_Renewal_Date_And_The_Contact_Route_Come_From_The_Clock_And_The_Policy()
    {
        var harness = new Harness(Month(allowance: 200, consumed: 200));

        var dto = await harness.Read();

        Assert.Equal(ClinicClock.FirstDayOfNextMonth(ClinicClock.ClinicToday()), dto.ResetsOn);
        Assert.Equal("forfait@example.tn", dto.ContactEmail);
        Assert.Equal("+216 70 000 000", dto.ContactPhone);
    }

    /// <summary>
    /// AC-2.7 — where the operator has published no contact details both fields are <b>null</b>, so the screen renders
    /// <b>no contact route at all</b> rather than an empty <c>mailto:</c>. A dead control is worse than an absent one.
    /// </summary>
    [Fact]
    public async Task An_Unconfigured_Contact_Route_Is_Null_Rather_Than_Empty()
    {
        var harness = new Harness(Month(allowance: 200, consumed: 200), contactEmail: null, contactPhone: null);

        var dto = await harness.Read();

        Assert.Null(dto.ContactEmail);
        Assert.Null(dto.ContactPhone);
    }

    /// <summary>
    /// AC-1.4 — the read states the sender state in <b>words</b> as well as by name, so nothing downstream has to
    /// translate an enum.
    /// </summary>
    [Theory]
    [InlineData(WhatsAppConnectionStatus.NotConnected, "NotConnected")]
    [InlineData(WhatsAppConnectionStatus.Connected, "Ready")]
    public async Task The_Sender_State_Is_Derived_And_Labelled(WhatsAppConnectionStatus status, string expected)
    {
        var harness = new Harness(Month(allowance: 200, consumed: 1), connectionStatus: status);

        var dto = await harness.Read();

        Assert.Equal(expected, dto.SenderState);
        Assert.False(string.IsNullOrWhiteSpace(dto.SenderStateLabel));
        Assert.NotEqual(dto.SenderState, dto.SenderStateLabel); // the label is French prose, not the enum name
    }

    /// <summary>
    /// AC-1.4's whole point, asserted on the derivation itself rather than through the read: <b>« connecté » is never
    /// presented as « prêt à envoyer »</b> once a template state exists. A cabinet under review is connected and cannot
    /// send a thing.
    ///
    /// <para>⚠️ Tested here and not through the query because two of the five inputs are unreachable from a
    /// <c>ClinicReminderSettings</c> today: <c>WhatsAppConnectionStatus.Error</c> has <b>no writer at all</b> in the
    /// product (Part 4's Meta classification is what will set it), and the four template columns arrive with it. The
    /// derivation is complete now so those parts add a <i>value</i> rather than a second rule.</para>
    ///
    /// <para>⚠️ A <b>null</b> template means « this deployment does not track one yet » and resolves to <c>Ready</c> on a
    /// connected cabinet — deliberately not <c>PendingReview</c>, which would tell every cabinet sending perfectly well
    /// today that its template is under review.</para>
    /// </summary>
    [Theory]
    [InlineData(WhatsAppConnectionStatus.NotConnected, null, MessagingSenderState.NotConnected)]
    [InlineData(WhatsAppConnectionStatus.NotConnected, WhatsAppTemplateStatus.Approved, MessagingSenderState.NotConnected)]
    [InlineData(WhatsAppConnectionStatus.Error, WhatsAppTemplateStatus.Approved, MessagingSenderState.Suspended)]
    [InlineData(WhatsAppConnectionStatus.Connected, null, MessagingSenderState.Ready)]
    [InlineData(WhatsAppConnectionStatus.Connected, WhatsAppTemplateStatus.Approved, MessagingSenderState.Ready)]
    [InlineData(WhatsAppConnectionStatus.Connected, WhatsAppTemplateStatus.NotSubmitted, MessagingSenderState.PendingReview)]
    [InlineData(WhatsAppConnectionStatus.Connected, WhatsAppTemplateStatus.PendingReview, MessagingSenderState.PendingReview)]
    [InlineData(WhatsAppConnectionStatus.Connected, WhatsAppTemplateStatus.Rejected, MessagingSenderState.TemplateRefused)]
    [InlineData(WhatsAppConnectionStatus.Connected, WhatsAppTemplateStatus.Paused, MessagingSenderState.TemplateRefused)]
    [InlineData(WhatsAppConnectionStatus.Connected, WhatsAppTemplateStatus.Disabled, MessagingSenderState.TemplateRefused)]
    public void Connected_Is_Never_Ready_While_The_Template_Is_Not(
        WhatsAppConnectionStatus connection, WhatsAppTemplateStatus? template, MessagingSenderState expected) =>
        Assert.Equal(expected, MessagingSender.From(connection, template));

    /// <summary>Every state has its own French sentence — no member falls through to the unknown fallback.</summary>
    [Fact]
    public void Every_Sender_State_Has_Its_Own_French_Label()
    {
        var labels = Enum.GetValues<MessagingSenderState>().Select(MessagingSender.Label).ToList();

        Assert.Equal(labels.Count, labels.Distinct().Count());
        Assert.DoesNotContain("inconnu", labels);
    }

    /// <summary>
    /// AC-2.5 / EC-12 — a failed read is a <c>Result.Failure</c>, <b>never</b> a zeroed DTO. The screen turns that into
    /// « je n'ai pas pu lire » with a retry; « 0 restant » here would be the opposite claim.
    /// </summary>
    [Fact]
    public async Task A_Failed_Read_Is_A_Failure_And_Not_A_Zeroed_Card()
    {
        var harness = new Harness(Month(allowance: 200, consumed: 10), failReads: true);

        var result = await harness.Send();

        Assert.True(result.IsFailure);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    // ---- The history (AC-2.3, AC-2.4, D-5) ---------------------------------------------------------

    /// <summary>
    /// AC-2.3 — the current month plus the twelve before it, newest first, when the cabinet is old enough and every
    /// month was counted.
    /// </summary>
    [Fact]
    public async Task The_History_Covers_Thirteen_Months_Newest_First()
    {
        var current = ClinicClock.CurrentMonthKey();
        var keys = new[] { current }
            .Concat(ClinicClock.PrecedingMonthKeys(current, 12))
            .ToList();

        var harness = new Harness(
            months: keys.Select(k => Month(allowance: 200, consumed: 5, monthKey: k)).ToList(),
            clinicCreatedAtUtc: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var dto = await harness.ReadHistory();

        Assert.Equal(13, dto.Months.Count);
        Assert.Equal(keys, dto.Months.Select(m => m.Month));
        Assert.All(dto.Months, m => Assert.True(m.Measured));
    }

    /// <summary>
    /// D-5 — a month <b>below the floor is omitted entirely</b>, not reported unmeasured. The floor is the later of the
    /// cabinet's creation month and its earliest counting row, so a practice that predates the rollout is never told we
    /// failed to count twelve months in which there was nothing to count.
    /// </summary>
    [Fact]
    public async Task Months_Before_The_First_Counting_Row_Are_Omitted_Rather_Than_Unmeasured()
    {
        var current = ClinicClock.CurrentMonthKey();
        var lastMonth = ClinicClock.PrecedingMonthKeys(current, 1).Single();

        // An old cabinet (created in 2020) whose first counting row is last month — the rollout case.
        var harness = new Harness(
            months: new List<ClinicMessagingMonth>
            {
                Month(allowance: 200, consumed: 12, monthKey: lastMonth),
                Month(allowance: 200, consumed: 3, monthKey: current),
            },
            clinicCreatedAtUtc: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var dto = await harness.ReadHistory();

        Assert.Equal(new[] { current, lastMonth }, dto.Months.Select(m => m.Month));
    }

    /// <summary>
    /// AC-2.4 — a month <b>before the cabinet existed</b> is not listed either, which is the floor's other term.
    /// </summary>
    [Fact]
    public async Task Months_Before_The_Cabinet_Existed_Are_Omitted()
    {
        var current = ClinicClock.CurrentMonthKey();
        var harness = new Harness(
            months: new List<ClinicMessagingMonth> { Month(allowance: 200, consumed: 3, monthKey: current) },
            clinicCreatedAtUtc: DateTime.UtcNow);

        var dto = await harness.ReadHistory();

        Assert.Equal(new[] { current }, dto.Months.Select(m => m.Month));
    }

    /// <summary>
    /// D-5 — a gap <b>inside</b> the range still reads « non mesuré », and that is the point: the floor removes months
    /// nobody promised to count, while a hole above it means the daily pass did not run (FR-1a).
    /// </summary>
    [Fact]
    public async Task A_Gap_Inside_The_Range_Reads_As_Unmeasured()
    {
        var current = ClinicClock.CurrentMonthKey();
        var preceding = ClinicClock.PrecedingMonthKeys(current, 2);
        var twoMonthsAgo = preceding[1];

        // Counted two months ago and this month; last month is a genuine hole.
        var harness = new Harness(
            months: new List<ClinicMessagingMonth>
            {
                Month(allowance: 200, consumed: 7, monthKey: twoMonthsAgo),
                Month(allowance: 200, consumed: 3, monthKey: current),
            },
            clinicCreatedAtUtc: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var dto = await harness.ReadHistory();

        var gap = dto.Months.Single(m => m.Month == preceding[0]);
        Assert.False(gap.Measured);
        Assert.Null(gap.Consumed);
        Assert.Null(gap.Allowance);
    }

    /// <summary>
    /// AC-2.3 — a past month shows the allowance that was <b>actually in force</b> then, from its own stored snapshot,
    /// never today's figure applied backwards (FR-1a).
    /// </summary>
    [Fact]
    public async Task A_Past_Month_Shows_The_Allowance_That_Was_In_Force_Then()
    {
        var current = ClinicClock.CurrentMonthKey();
        var lastMonth = ClinicClock.PrecedingMonthKeys(current, 1).Single();

        var harness = new Harness(
            months: new List<ClinicMessagingMonth>
            {
                Month(allowance: 150, consumed: 150, monthKey: lastMonth),
                Month(allowance: 500, consumed: 12, monthKey: current),
            },
            clinicCreatedAtUtc: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var dto = await harness.ReadHistory();

        Assert.Equal(150, dto.Months.Single(m => m.Month == lastMonth).Allowance);
        Assert.Equal(500, dto.Months.Single(m => m.Month == current).Allowance);
    }

    // ---- Fixtures ----------------------------------------------------------------------------------

    private static ClinicMessagingMonth Month(int allowance, int consumed, string? monthKey = null)
    {
        var row = ClinicMessagingMonth.For(
            ClinicId, monthKey ?? ClinicClock.CurrentMonthKey(), allowance, DateTime.UtcNow);
        for (var i = 0; i < consumed; i++)
        {
            row.RecordSend(DateTime.UtcNow);
        }

        return row;
    }

    /// <summary>
    /// A cabinet with a chosen creation date. D-5's floor is a function of <c>Clinic.CreatedAt</c>, which every entity
    /// in this codebase stamps from the clock <b>inside its constructor</b> — so an old-cabinet fixture cannot be built
    /// any other way. Test-only, and named so rather than buried in the harness.
    /// </summary>
    private static Clinic ClinicCreatedAt(DateTime createdAtUtc)
    {
        var clinic = new Clinic(ClinicId, "Cabinet Test", city: "Tunis");
        typeof(Clinic).GetProperty(nameof(Clinic.CreatedAt))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(clinic, new object[] { createdAtUtc });
        return clinic;
    }

    private sealed class Harness
    {
        private readonly GetReminderAllowanceQueryHandler _current;
        private readonly GetReminderAllowanceHistoryQueryHandler _history;

        public Harness(
            ClinicMessagingMonth? month,
            string? contactEmail = "forfait@example.tn",
            string? contactPhone = "+216 70 000 000",
            WhatsAppConnectionStatus connectionStatus = WhatsAppConnectionStatus.Connected,
            bool failReads = false)
            : this(
                month is null ? new List<ClinicMessagingMonth>() : new List<ClinicMessagingMonth> { month },
                DateTime.UtcNow, contactEmail, contactPhone, connectionStatus, failReads)
        {
        }

        public Harness(List<ClinicMessagingMonth> months, DateTime clinicCreatedAtUtc)
            : this(months, clinicCreatedAtUtc, "forfait@example.tn", "+216 70 000 000",
                WhatsAppConnectionStatus.Connected, failReads: false)
        {
        }

        private Harness(
            List<ClinicMessagingMonth> months,
            DateTime clinicCreatedAtUtc,
            string? contactEmail,
            string? contactPhone,
            WhatsAppConnectionStatus connectionStatus,
            bool failReads)
        {
            var allowances = new Mock<IMessagingAllowanceRepository>();
            if (failReads)
            {
                allowances.Setup(a => a.GetMonthAsync(
                        It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("database unreachable"));
            }
            else
            {
                allowances.Setup(a => a.GetMonthAsync(
                        ClinicId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((Guid _, string key, CancellationToken _) =>
                        months.FirstOrDefault(m => m.MonthKey == key));
            }

            allowances.Setup(a => a.GetMonthsAsync(ClinicId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, string from, CancellationToken _) => months
                    .Where(m => string.CompareOrdinal(m.MonthKey, from) >= 0)
                    .OrderBy(m => m.MonthKey, StringComparer.Ordinal)
                    .ToList());

            var settingsRow = new ClinicReminderSettings(ClinicId);
            if (connectionStatus == WhatsAppConnectionStatus.Connected)
            {
                settingsRow.ApplyWhatsAppConnection("waba-1", "phone-1");
            }

            var settings = new Mock<IClinicReminderSettingsRepository>();
            settings.Setup(s => s.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(settingsRow);

            var clinics = new Mock<IClinicRepository>();
            clinics.Setup(c => c.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ClinicCreatedAt(clinicCreatedAtUtc));

            var resolver = new Mock<ICurrentClinicResolver>();
            resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Success(ClinicId));

            var policy = new Mock<IMessagingAllowancePolicy>();
            policy.SetupGet(p => p.ContactEmail).Returns(contactEmail);
            policy.SetupGet(p => p.ContactPhone).Returns(contactPhone);

            // AC-1.1's `canConnect`. A default mock answers false, which is what a deployment with no Meta
            // credentials reports — the figures below are unaffected, and the offer to connect has its own cases.
            _current = new GetReminderAllowanceQueryHandler(
                allowances.Object, settings.Object, resolver.Object, policy.Object,
                Mock.Of<IVendorMessagingAvailability>(),
                NullLogger<GetReminderAllowanceQueryHandler>.Instance);

            _history = new GetReminderAllowanceHistoryQueryHandler(
                allowances.Object, clinics.Object, resolver.Object,
                NullLogger<GetReminderAllowanceHistoryQueryHandler>.Instance);
        }

        public Task<Result<Application.DTOs.ReminderAllowanceDto>> Send() =>
            _current.Handle(new GetReminderAllowanceQuery(), CancellationToken.None);

        public async Task<Application.DTOs.ReminderAllowanceDto> Read()
        {
            var result = await Send();
            Assert.True(result.IsSuccess, result.Error);
            return result.Value!;
        }

        public async Task<Application.DTOs.ReminderAllowanceHistoryDto> ReadHistory()
        {
            var result = await _history.Handle(
                new GetReminderAllowanceHistoryQuery(), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
            return result.Value!;
        }
    }
}
