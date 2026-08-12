using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Messaging;

/// <summary>
/// The WhatsApp reminder forfait's enforcement (<c>vendor-whatsapp-messaging-quota</c> FR-4, Part 1 step 12).
///
/// <para><b>Most of this class asserts what the gate must NOT do</b>, and that is where its value is: it sits in front
/// of every queued WhatsApp reminder on the hosted deployment, so a wrong « park » verdict does not degrade a feature —
/// it silently stops a practice's patients being reminded. Hence the SMS case, the capability-off case and the
/// zero-query assertions outnumbering the two refusals.</para>
///
/// <para>⚠️ <b>Every case pins its own clinic-local day.</b> The gate takes one and derives the month from it, so
/// « which month is this send charged to » is answerable rather than dependent on when the suite runs (EC-7).</para>
/// </summary>
public class OutboxMessagingGateTests
{
    private static readonly Guid Clinic = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinic = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // 12 August 2026, clinic-local. Mid-month, so the renewal date in the parked sentence is 1 September.
    private static readonly DateTime MidAugust = new(2026, 8, 12);
    private static readonly DateTime CreatedAt = new(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

    private sealed class Harness
    {
        public Mock<IVendorMessagingAvailability> Availability { get; } = new();
        public Mock<IMessagingAllowanceRepository> Allowances { get; } = new();

        /// <summary>Reads observed, so a test can assert the gate issued <b>no query at all</b>.</summary>
        public int MonthReads { get; private set; }

        public Harness(bool sellsVendorMessaging = true)
        {
            Availability.SetupGet(a => a.SellsVendorMessaging).Returns(sellsVendorMessaging);

            Allowances
                .Setup(r => r.GetMonthAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => MonthReads++)
                .ReturnsAsync((ClinicMessagingMonth?)null);
        }

        public void WithMonth(Guid clinicId, string monthKey, int allowance, int consumed)
        {
            var month = ClinicMessagingMonth.For(clinicId, monthKey, allowance, CreatedAt);
            for (var i = 0; i < consumed; i++)
            {
                month.RecordSend(CreatedAt);
            }

            Allowances
                .Setup(r => r.GetMonthAsync(clinicId, monthKey, It.IsAny<CancellationToken>()))
                .Callback(() => MonthReads++)
                .ReturnsAsync(month);
        }

        public OutboxMessagingGate Gate(DateTime? clinicToday = null) =>
            new(Availability.Object, Allowances.Object, clinicToday ?? MidAugust);
    }

    // ---- What it must not touch ------------------------------------------------------------------

    /// <summary>
    /// [AC-4.6] An SMS reminder for the same appointment is untouched — it is not paid for out of this forfait — and
    /// it is never even looked up.
    /// </summary>
    [Fact]
    public async Task An_Sms_Row_Is_Never_Consulted_Even_On_An_Exhausted_Cabinet()
    {
        var harness = new Harness();
        harness.WithMonth(Clinic, "2026-08", allowance: 100, consumed: 100);

        Assert.Null(await harness.Gate().ReviewAsync(NotificationType.SMS, Clinic));
        Assert.Equal(0, harness.MonthReads);
    }

    /// <summary>
    /// [EC-16] Where the deployment does not sell vendor messaging the gate reads <b>nothing at all</b>, so the two
    /// other deployment kinds pay for none of this — « absent, not present-and-refusing » made structural.
    /// </summary>
    [Fact]
    public async Task The_Capability_Off_Issues_Zero_Queries()
    {
        var harness = new Harness(sellsVendorMessaging: false);
        harness.WithMonth(Clinic, "2026-08", allowance: 100, consumed: 100);

        Assert.Null(await harness.Gate().ReviewAsync(NotificationType.WhatsApp, Clinic));
        Assert.Equal(0, harness.MonthReads);
    }

    /// <summary>
    /// A row with no cabinet has no forfait to consult — legacy rows enqueued before per-clinic settings existed carry
    /// no <c>ClinicId</c>. The same reason the subscription gate lets such a row through.
    /// </summary>
    [Fact]
    public async Task A_Row_With_No_Cabinet_Passes_Without_A_Query()
    {
        var harness = new Harness();

        Assert.Null(await harness.Gate().ReviewAsync(NotificationType.WhatsApp, null));
        Assert.Equal(0, harness.MonthReads);
    }

    [Fact]
    public async Task A_Cabinet_With_Messages_Left_Sends()
    {
        var harness = new Harness();
        harness.WithMonth(Clinic, "2026-08", allowance: 200, consumed: 143);

        Assert.Null(await harness.Gate().ReviewAsync(NotificationType.WhatsApp, Clinic));
    }

    // ---- The two refusals, each under its own reason ---------------------------------------------

    [Fact]
    public async Task An_Exhausted_Cabinet_Is_Held_Under_Its_Own_Reason() // [AC-4.1]
    {
        var harness = new Harness();
        harness.WithMonth(Clinic, "2026-08", allowance: 200, consumed: 200);

        var parked = await harness.Gate().ReviewAsync(NotificationType.WhatsApp, Clinic);

        Assert.NotNull(parked);
        Assert.Equal(OutboxBlockReason.MessagingAllowanceExhausted, parked!.Reason);
        // The renewal date is a fact about the FORFAIT, and it is the 1st of the next Tunisian month.
        Assert.Contains("01/09/2026", parked.Sentence);
        // It says the send is WAITING, not failed: nothing was lost and nothing was attempted.
        Assert.Contains("en attente", parked.Sentence);
    }

    /// <summary>
    /// [AC-4.3] No allowance record is held under a <b>distinct</b> reason and a distinct sentence. It is our own
    /// bookkeeping fault, not a limit the practice reached, so it must never be presented as « épuisé » — there is
    /// nothing for them to have spent, and the remedy is us restoring the row.
    /// </summary>
    [Fact]
    public async Task A_Missing_Allowance_Record_Is_Held_Under_A_Different_Reason_And_Sentence()
    {
        var harness = new Harness(); // GetMonthAsync answers null by default

        var parked = await harness.Gate().ReviewAsync(NotificationType.WhatsApp, Clinic);

        Assert.NotNull(parked);
        Assert.Equal(OutboxBlockReason.MessagingAllowanceMissing, parked!.Reason);
        Assert.DoesNotContain("épuisé", parked.Sentence);
        // No date either: there is no renewal that would fix it, so naming one would leave the practice waiting for
        // something that will not happen.
        Assert.DoesNotContain("2026", parked.Sentence);
    }

    /// <summary>
    /// The two reasons and the two sentences are genuinely different pairs — the property that stops one refusal's
    /// wording drifting onto the other's code, which is the defect <c>MessagingRefusals</c> exists to prevent.
    /// </summary>
    [Fact]
    public async Task The_Two_Refusals_Are_Distinct_In_Both_Reason_And_Wording()
    {
        var exhausted = new Harness();
        exhausted.WithMonth(Clinic, "2026-08", allowance: 10, consumed: 10);
        var missing = new Harness();

        var first = await exhausted.Gate().ReviewAsync(NotificationType.WhatsApp, Clinic);
        var second = await missing.Gate().ReviewAsync(NotificationType.WhatsApp, Clinic);

        Assert.NotEqual(first!.Reason, second!.Reason);
        Assert.NotEqual(first.Sentence, second.Sentence);
    }

    [Fact]
    public async Task A_Zero_Allowance_Is_Exhausted_Rather_Than_Missing()
    {
        // A row exists, so this is a decision the vendor made — not our gap. It must be the exhausted reason.
        var harness = new Harness();
        harness.WithMonth(Clinic, "2026-08", allowance: 0, consumed: 0);

        var parked = await harness.Gate().ReviewAsync(NotificationType.WhatsApp, Clinic);

        Assert.Equal(OutboxBlockReason.MessagingAllowanceExhausted, parked!.Reason);
    }

    // ---- Term ordering — the slot Part 4 fills ---------------------------------------------------

    /// <summary>
    /// The ordered terms resolve <b>allowance-missing before allowance-exhausted</b>, and Part 4's template term is
    /// declared above both. <b>The order is the wording, not an implementation detail</b>: a cabinet meeting two
    /// conditions must be told the one it can act on.
    ///
    /// <para>Asserted here as the property that holds today — a missing row is answered with « introuvable » and never
    /// with « épuisé », even though a missing row also means nothing can be sent. When Part 4 adds the template term
    /// this class gains the case that a cabinet which is <i>both</i> template-not-ready and exhausted is told about the
    /// template.</para>
    /// </summary>
    [Fact]
    public async Task A_Missing_Row_Is_Answered_As_Missing_Rather_Than_As_Exhausted()
    {
        var harness = new Harness();

        var parked = await harness.Gate().ReviewAsync(NotificationType.WhatsApp, Clinic);

        Assert.Equal(OutboxBlockReason.MessagingAllowanceMissing, parked!.Reason);
        Assert.NotEqual(OutboxBlockReason.MessagingAllowanceExhausted, parked.Reason);
    }

    // ---- The per-tick cache ---------------------------------------------------------------------

    /// <summary>
    /// One instance per tick, one query per cabinet — a 50-row batch from one practice must not issue 50 identical
    /// reads. Per <i>cabinet</i>, not per gate, so a second practice in the same batch is still consulted.
    /// </summary>
    [Fact]
    public async Task One_Query_Per_Cabinet_Per_Tick_However_Many_Rows()
    {
        var harness = new Harness();
        harness.WithMonth(Clinic, "2026-08", allowance: 200, consumed: 10);
        harness.WithMonth(OtherClinic, "2026-08", allowance: 200, consumed: 200);
        var gate = harness.Gate();

        for (var i = 0; i < 20; i++)
        {
            await gate.ReviewAsync(NotificationType.WhatsApp, Clinic);
        }

        Assert.Equal(1, harness.MonthReads);

        Assert.NotNull(await gate.ReviewAsync(NotificationType.WhatsApp, OtherClinic));
        Assert.Equal(2, harness.MonthReads);
    }

    // ---- EC-7: the month is the clinic's ---------------------------------------------------------

    /// <summary>
    /// [EC-7] The month the gate meters against is the <b>clinic-local</b> one, fixed for the whole tick by the day
    /// the caller passes in. 31 August and 1 September are different forfaits, and a send at 23:59 Tunis on the 31st
    /// belongs to August.
    /// </summary>
    [Theory]
    [InlineData(2026, 8, 31, "2026-08")]
    [InlineData(2026, 9, 1, "2026-09")]
    public async Task The_Month_Metered_Is_The_Clinic_Local_One(int year, int month, int day, string expectedKey)
    {
        var harness = new Harness();
        var gate = harness.Gate(new DateTime(year, month, day));

        Assert.Equal(expectedKey, gate.MonthKey);

        await gate.ReviewAsync(NotificationType.WhatsApp, Clinic);

        // The read is made against that month's key and no other — the assertion that fails if the gate reads the
        // clock itself or derives the month from UTC.
        harness.Allowances.Verify(
            r => r.GetMonthAsync(Clinic, expectedKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A cabinet exhausted in August is not exhausted in September, and the gate says so purely from the day it was
    /// handed — no clock, no rollover pass required.
    /// </summary>
    [Fact]
    public async Task A_New_Month_Is_A_New_Forfait()
    {
        var harness = new Harness();
        harness.WithMonth(Clinic, "2026-08", allowance: 100, consumed: 100);
        harness.WithMonth(Clinic, "2026-09", allowance: 100, consumed: 0);

        Assert.NotNull(await harness.Gate(new DateTime(2026, 8, 31)).ReviewAsync(NotificationType.WhatsApp, Clinic));
        Assert.Null(await harness.Gate(new DateTime(2026, 9, 1)).ReviewAsync(NotificationType.WhatsApp, Clinic));
    }
}
