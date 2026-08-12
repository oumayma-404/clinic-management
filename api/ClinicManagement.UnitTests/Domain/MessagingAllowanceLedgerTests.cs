using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// The WhatsApp reminder forfait's fold (<c>vendor-whatsapp-messaging-quota</c> FR-2, Part 1).
///
/// <para><b>The highest-value class in the feature.</b> Every screen, the enforcement gate, the warning thresholds,
/// the console portfolio and <c>messaging-report</c> all read the figure this produces, so an error here is wrong
/// everywhere at once and visible nowhere in particular — the same argument <c>SubscriptionLedgerTests</c> makes about
/// its own subject.</para>
///
/// <para><b>⚠️ There is no <c>DateTime.UtcNow</c> anywhere in this file, and no clock is passed in.</b> The fold takes
/// the month as a parameter, so every case names its month outright. A test that read the clock would agree with a
/// clock-dependent fold by construction — the trap <c>ClinicClockTests</c> documents — and it is exactly the property
/// under test here: identical entries must fold to the same figure whenever they are recomputed.</para>
/// </summary>
public class MessagingAllowanceLedgerTests
{
    private static readonly Guid Clinic = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // Fixed instants. Order matters to the fold's tie-break, so they are spelled out rather than derived.
    private static readonly DateTime June = new(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime July = new(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime August = new(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

    private static MessagingAllowanceEntry Standing(int messages, string month, DateTime recordedAtUtc) =>
        MessagingAllowanceEntry.Create(
            Clinic, MessagingAllowanceKind.Standing, messages, month, recordedAtUtc);

    private static MessagingAllowanceEntry TopUp(int messages, string month, DateTime recordedAtUtc) =>
        MessagingAllowanceEntry.Create(
            Clinic, MessagingAllowanceKind.TopUp, messages, month, recordedAtUtc);

    private static int? Fold(string month, params MessagingAllowanceEntry[] entries) =>
        MessagingAllowanceLedger.Fold(entries.Select(e => e.ToLedgerEntry()), month);

    // ---- The distinction that must never collapse: null vs 0 ---------------------------------------

    /// <summary>
    /// [AC-4.3] No entry folds to <b>null</b>, never to 0. They are opposite facts — « the vendor allowed this
    /// practice nothing » against « we have no record of what they were allowed » — and the second is held under its
    /// own reason and its own French sentence. Collapsing them would tell a practice « votre forfait est épuisé »
    /// about a row nobody ever wrote.
    /// </summary>
    [Fact]
    public void A_Cabinet_With_No_Entry_Folds_To_Null_Not_Zero()
    {
        Assert.Null(Fold("2026-08"));
    }

    [Fact]
    public void A_Standing_Zero_Folds_To_Zero_Which_Is_A_Real_Decision()
    {
        // « ce cabinet n'envoie pas de rappels WhatsApp » is a decision the vendor can make, and it is not the same
        // state as having no record at all.
        Assert.Equal(0, Fold("2026-08", Standing(0, "2026-08", August)));
    }

    [Fact]
    public void A_Month_Before_Every_Entry_Folds_To_Null()
    {
        // A closed month the forfait never reached is unmeasured, not zeroed — which is what keeps « non mesuré »
        // reachable on the history screen (AC-2.4).
        Assert.Null(Fold("2026-05", Standing(200, "2026-06", June)));
    }

    // ---- The fold itself -------------------------------------------------------------------------

    [Fact]
    public void A_Standing_Figure_Carries_Forward_Until_Superseded()
    {
        var first = Standing(200, "2026-06", June);
        var second = Standing(500, "2026-08", August);

        Assert.Equal(200, Fold("2026-06", first, second));
        Assert.Equal(200, Fold("2026-07", first, second));
        Assert.Equal(500, Fold("2026-08", first, second));
        // Carries on into a month no entry names.
        Assert.Equal(500, Fold("2026-12", first, second));
    }

    [Fact]
    public void A_Standing_Figure_Replaces_Rather_Than_Accumulating()
    {
        // The defect this pins is a fold that sums standing entries: two figures of 200 and 500 must be 500, not 700.
        Assert.Equal(500, Fold("2026-08", Standing(200, "2026-08", June), Standing(500, "2026-08", August)));
    }

    [Fact]
    public void A_Top_Up_Adds_To_Its_Own_Month_And_No_Other() // [AC-6.1]
    {
        var standing = Standing(200, "2026-06", June);
        var topUp = TopUp(50, "2026-08", August);

        Assert.Equal(200, Fold("2026-07", standing, topUp));
        Assert.Equal(250, Fold("2026-08", standing, topUp));
        // Not carried forward: a top-up is spent or lapsed with its month.
        Assert.Equal(200, Fold("2026-09", standing, topUp));
    }

    [Fact]
    public void Several_Top_Ups_In_One_Month_All_Count()
    {
        Assert.Equal(
            330,
            Fold("2026-08", Standing(200, "2026-08", June), TopUp(100, "2026-08", July), TopUp(30, "2026-08", August)));
    }

    [Fact]
    public void A_Top_Up_With_No_Standing_Entry_Behind_It_Still_Yields_A_Figure()
    {
        // A cabinet the vendor has given messages to is not a cabinet with no allowance record, so this must not be
        // null — the gate would otherwise refuse a practice holding messages it was told it had.
        Assert.Equal(50, Fold("2026-08", TopUp(50, "2026-08", August)));
    }

    // ---- Clock-freedom and idempotence -----------------------------------------------------------

    /// <summary>
    /// [FR-2] Folding the same ledger twice yields the same figure, and the order the entries arrive in does not
    /// change it. Both are what let the stored snapshot be re-derived by <c>verify-schema</c> instead of trusted.
    /// </summary>
    [Fact]
    public void The_Fold_Is_Idempotent_And_Order_Independent()
    {
        var entries = new[]
        {
            Standing(200, "2026-06", June),
            TopUp(50, "2026-08", August),
            Standing(300, "2026-07", July)
        };

        var forward = MessagingAllowanceLedger.Fold(entries.Select(e => e.ToLedgerEntry()), "2026-08");
        var reversed = MessagingAllowanceLedger.Fold(entries.Reverse().Select(e => e.ToLedgerEntry()), "2026-08");

        Assert.Equal(350, forward);
        Assert.Equal(forward, reversed);
    }

    // ---- Cancellation (AC-7.4) and its deliberate asymmetry with AC-6.4 ---------------------------

    /// <summary>
    /// [AC-7.4][EC-4] A cancelled entry contributes nothing to <b>every</b> month it fed, <b>the current one
    /// included</b> — deliberately the opposite of AC-6.4, where a *lowering* waits for the next month (AC-7.4a). The
    /// distinction is that a lowering is a decision about the future while a cancellation says the entry should never
    /// have existed.
    /// </summary>
    [Fact]
    public void A_Cancelled_Entry_Stops_Feeding_Every_Month_Including_The_Current_One()
    {
        var first = Standing(200, "2026-06", June);
        var raise = Standing(500, "2026-07", July);

        Assert.Equal(500, Fold("2026-08", first, raise));

        raise.Cancel("Enregistré sur le mauvais cabinet.", "console|vendor", August);

        // The raise is gone from July onwards, so August falls back to the figure that preceded it — not to null, and
        // not to the raise's value with the row merely hidden.
        Assert.Equal(200, Fold("2026-07", first, raise));
        Assert.Equal(200, Fold("2026-08", first, raise));
    }

    [Fact]
    public void Cancelling_A_Cabinets_Only_Entry_Returns_It_To_Having_No_Record()
    {
        var only = Standing(200, "2026-08", August);
        only.Cancel("Créé par erreur.", "console|vendor", August);

        // Null rather than 0: the ledger now reaches this month with nothing, which is AC-4.3's state exactly.
        Assert.Null(Fold("2026-08", only));
    }

    [Fact]
    public void Cancelling_A_Top_Up_Leaves_The_Standing_Figure_Standing()
    {
        var standing = Standing(200, "2026-08", June);
        var topUp = TopUp(50, "2026-08", August);
        topUp.Cancel("Le virement n'est jamais arrivé.", "console|vendor", August);

        Assert.Equal(200, Fold("2026-08", standing, topUp));
    }

    [Fact]
    public void An_Already_Cancelled_Entry_Refuses_A_Second_Cancellation() // [AC-7.5]
    {
        var entry = Standing(200, "2026-08", August);
        entry.Cancel("Première annulation.", "console|vendor", August);

        // Refused rather than silently re-stamped: the row holds one motif, one author and one moment, so a second
        // cancellation would overwrite a colleague's reasoning with no trace of it anywhere.
        Assert.Throws<InvalidOperationException>(
            () => entry.Cancel("Deuxième annulation.", "console|other", August));
    }

    [Fact]
    public void A_Cancellation_Requires_A_Motif() // [AC-7.1]
    {
        var entry = Standing(200, "2026-08", August);

        Assert.Throws<ArgumentException>(() => entry.Cancel("   ", "console|vendor", August));
    }

    // ---- AC-6.4a: the server decides the effective month -----------------------------------------

    /// <summary>
    /// [AC-6.3][AC-6.4][AC-6.4a][EC-3] A <b>raise</b> is effective this month; a <b>lowering</b> waits for the next
    /// one. The vendor states an amount and never a month, so this is the server's decision and it is made against
    /// the ledger.
    /// </summary>
    [Theory]
    [InlineData(500, "2026-08")] // a raise — immediately
    [InlineData(200, "2026-08")] // unchanged — treated as this month; « prend effet le mois prochain » would puzzle
    [InlineData(100, "2026-09")] // a lowering — next month
    public void The_Effective_Month_Depends_On_Whether_The_Figure_Rises_Or_Falls(int newFigure, string expected)
    {
        var ledger = new[] { Standing(200, "2026-06", June).ToLedgerEntry() };

        Assert.Equal(
            expected,
            MessagingAllowanceLedger.EffectiveMonthFor(ledger, newFigure, "2026-08", "2026-09"));
    }

    [Fact]
    public void The_First_Standing_Figure_Of_A_Cabinet_Is_Effective_Immediately()
    {
        // Nothing is in force, so nothing is being lowered — a cabinet's first forfait must not wait a month.
        Assert.Equal(
            "2026-08",
            MessagingAllowanceLedger.EffectiveMonthFor(Array.Empty<MessagingAllowanceLedgerEntry>(), 200, "2026-08", "2026-09"));
    }

    /// <summary>
    /// The effective month is measured against the <b>standing</b> figure alone, never the folded total. Comparing
    /// against « standing + top-up » would read an ordinary raise as a lowering and defer it by a month for a reason
    /// nobody chose.
    /// </summary>
    [Fact]
    public void A_Top_Up_Does_Not_Make_A_Raise_Look_Like_A_Lowering()
    {
        var ledger = new[]
        {
            Standing(200, "2026-06", June).ToLedgerEntry(),
            TopUp(400, "2026-08", August).ToLedgerEntry()
        };

        // The folded total for August is 600; the standing figure is 200. Raising the standing figure to 300 is a
        // raise, and must land this month.
        Assert.Equal(600, MessagingAllowanceLedger.Fold(ledger, "2026-08"));
        Assert.Equal("2026-08", MessagingAllowanceLedger.EffectiveMonthFor(ledger, 300, "2026-08", "2026-09"));
    }

    [Fact]
    public void A_Cancelled_Entry_Is_Not_The_Figure_In_Force()
    {
        var superseded = Standing(200, "2026-06", June);
        var current = Standing(500, "2026-07", July);
        current.Cancel("Erreur de saisie.", "console|vendor", August);

        var ledger = new[] { superseded.ToLedgerEntry(), current.ToLedgerEntry() };

        Assert.Equal(200, MessagingAllowanceLedger.StandingInForce(ledger, "2026-08"));
        // 300 against a live figure of 200 is a raise, even though it is below the cancelled 500.
        Assert.Equal("2026-08", MessagingAllowanceLedger.EffectiveMonthFor(ledger, 300, "2026-08", "2026-09"));
    }

    // ---- Remaining is floored (AC-2.1, AC-7.4) ---------------------------------------------------

    /// <summary>
    /// [AC-7.4] Remaining is <c>max(0, allowance − consumed)</c> and never negative. A cancellation can legitimately
    /// put consumption <i>above</i> the allowance — the messages were sent and the vendor paid for them — and the
    /// month then reads « épuisé » rather than « −17 rappels », which is not a quantity a practice can act on.
    /// </summary>
    [Fact]
    public void Remaining_Is_Never_Negative_After_A_Cancellation_Lowers_The_Allowance()
    {
        var month = ClinicMessagingMonth.For(Clinic, "2026-08", 200, August);
        for (var i = 0; i < 150; i++)
        {
            month.RecordSend(August);
        }

        Assert.Equal(50, month.RemainingMessages);
        Assert.False(month.IsExhausted);

        // The vendor cancels the entry that raised this cabinet to 200; the fold now says 100.
        month.SetAllowance(100, August);

        Assert.Equal(150, month.ConsumedMessages);
        Assert.Equal(0, month.RemainingMessages);
        Assert.True(month.IsExhausted);
    }

    [Fact]
    public void A_Zero_Allowance_Is_Exhausted_From_The_First_Tick() // [AC-8.2]
    {
        var month = ClinicMessagingMonth.For(Clinic, "2026-08", 0, August);

        Assert.True(month.IsExhausted);
        Assert.Equal(0, month.RemainingMessages);
    }

    [Fact]
    public void Setting_The_Allowance_Leaves_Consumption_Alone()
    {
        var month = ClinicMessagingMonth.For(Clinic, "2026-08", 200, August);
        month.RecordSend(August);
        month.SetAllowance(400, August);

        Assert.Equal(1, month.ConsumedMessages);
    }

    // ---- The entry's own guards ------------------------------------------------------------------

    /// <summary>
    /// A malformed month key does not fail — it silently never matches, so the entry folds into no month at all and
    /// the cabinet reads as having no allowance. That is why it is refused at construction rather than trusted.
    /// </summary>
    [Theory]
    [InlineData("2026-8")]
    [InlineData("2026-13")]
    [InlineData("2026-08-01")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Malformed_Effective_Month_Is_Refused(string month)
    {
        Assert.Throws<ArgumentException>(() => Standing(200, month, August));
    }

    [Fact]
    public void A_Top_Up_Of_Nothing_Is_Refused_While_A_Standing_Zero_Is_Not()
    {
        // A top-up of zero is indistinguishable on screen from one the vendor meant to make; a standing zero is a
        // decision. The asymmetry is deliberate.
        Assert.Throws<ArgumentException>(() => TopUp(0, "2026-08", August));
        Assert.Equal(0, Standing(0, "2026-08", August).Messages);
    }

    [Fact]
    public void A_Negative_Figure_Is_Refused()
    {
        Assert.Throws<ArgumentException>(() => Standing(-1, "2026-08", August));
    }

    [Fact]
    public void A_Complimentary_Allocation_Carries_No_Amount_Rather_Than_Zero() // [AC-6.6]
    {
        var entry = MessagingAllowanceEntry.Create(
            Clinic, MessagingAllowanceKind.TopUp, 100, "2026-08", August);

        // Null, not 0m: an amount of 0,000 DT reads as a transaction that happened for nothing.
        Assert.Null(entry.Amount);
    }
}
