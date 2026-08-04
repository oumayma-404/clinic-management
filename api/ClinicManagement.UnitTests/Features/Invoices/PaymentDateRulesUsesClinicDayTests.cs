using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Invoices;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// [J3] <c>PaymentDateRules</c> runs on the <b>clinic's</b> calendar day, not UTC's.
///
/// <para>
/// P6 made <c>ClinicClock</c> the single authority on « what day is it », swept the numbering and la caisse's
/// default — and left the one validator <b>every money date flows through</b> on <c>DateTime.UtcNow.Date</c>.
/// Tunisia is UTC+1, so that ran the clinic's calendar from 01:00 to 01:00: between 00:00 and 01:00 Tunis the
/// date the client itself pre-filled with <c>todayLocalIso()</c> — the browser's own local day — was refused
/// here as « dans le futur ». The client and the server disagreed about what day it was, and the only screen
/// that could reveal it is the one a dentist uses at the end of a late session.
/// </para>
/// <para>
/// ⚠️ <b>Every case uses a fixed instant</b>, which is the whole point. A test that computed « today » from a
/// freshly-read <c>DateTime.UtcNow</c> would evaluate the same expression the bug does and agree with it by
/// construction — and would additionally flake for one hour every night. This is the lesson
/// <c>ClinicClockTests</c> records for § 4.2 (AC-P6.9), applied to the validator that consumes it.
/// </para>
/// </summary>
public class PaymentDateRulesUsesClinicDayTests
{
    private const string Field = "La date du paiement";

    /// <summary>
    /// 23:30 UTC on 31 March 2026 — i.e. <b>00:30 on 1 April</b> in Tunis. The one hour of every day where the
    /// clinic's date and UTC's date differ, and the entire subject of this class.
    /// </summary>
    private static readonly DateTime HalfPastMidnightTunis = new(2026, 3, 31, 23, 30, 0, DateTimeKind.Utc);

    /// <summary>The clinic-local calendar day at that instant: 1 April 2026.</summary>
    private static readonly DateTime ClinicDay = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

    // [J3] The defect, pinned. At 00:30 Tunis the clinic is on 1 April; a payment the client dated 1 April must
    // be ACCEPTED. Under `DateTime.UtcNow.Date` the server still thought it was 31 March and refused it.
    [Fact]
    public void A_Payment_Dated_Today_At_0030_Tunis_Is_Accepted()
    {
        var error = PaymentDateRules.Validate(ClinicDay, Field, HalfPastMidnightTunis);

        Assert.Null(error);
    }

    // [J3] Sanity-check the premise rather than asserting it from memory: the fixed instant really is the
    // cross-over hour — UTC says 31 March, the clinic says 1 April. If this ever fails, the case above is
    // testing nothing.
    [Fact]
    public void The_Fixture_Really_Is_The_Hour_Where_The_Two_Dates_Differ()
    {
        Assert.Equal(new DateTime(2026, 3, 31), HalfPastMidnightTunis.Date);
        Assert.Equal(new DateTime(2026, 4, 1), ClinicClock.ClinicToday(HalfPastMidnightTunis));
    }

    // [J3] The guard is not merely loosened: the clinic's *tomorrow* is still refused. A future date is counted
    // in the balance today and absent from the caisse until it arrives.
    [Fact]
    public void The_Clinic_Tomorrow_Is_Still_Refused()
    {
        var error = PaymentDateRules.Validate(ClinicDay.AddDays(1), Field, HalfPastMidnightTunis);

        Assert.NotNull(error);
        Assert.Contains("futur", error);
    }

    // [J3] Mid-afternoon, where UTC and the clinic agree on the date, is unchanged — the fix must not move the
    // boundary for the 23 hours a day that were already correct.
    [Fact]
    public void Midday_Is_Unaffected()
    {
        var noonUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.Null(PaymentDateRules.Validate(ClinicDay, Field, noonUtc));
        Assert.NotNull(PaymentDateRules.Validate(ClinicDay.AddDays(1), Field, noonUtc));
    }

    // [J2] An omitted JSON key posts `0001-01-01`. Such a row moves the collected total and is invisible in
    // every cash window forever — a permanent, silent divergence between the column and the row sums.
    [Fact]
    public void An_Unset_Date_Is_Refused()
    {
        var error = PaymentDateRules.Validate(default, Field, HalfPastMidnightTunis);

        Assert.NotNull(error);
        Assert.Contains("absente ou invalide", error);
    }

    // [J2] Anything at or before the year-2000 floor is garbage rather than a real clinic date.
    [Fact]
    public void A_PreFloor_Date_Is_Refused()
    {
        var error = PaymentDateRules.Validate(
            new DateTime(1999, 12, 31, 0, 0, 0, DateTimeKind.Utc), Field, HalfPastMidnightTunis);

        Assert.NotNull(error);
    }

    // [J2] The field label is echoed back so one shared validator can serve « La date du paiement », « La date
    // de remboursement » and « La date de paiement » without inventing a message per caller.
    [Fact]
    public void The_Error_Names_The_Field_It_Rejected()
    {
        var error = PaymentDateRules.Validate(default, "La date de remboursement", HalfPastMidnightTunis);

        Assert.NotNull(error);
        Assert.StartsWith("La date de remboursement", error);
    }

    // [J3] The clinic day is *ahead* of UTC, so the acceptance window is one hour WIDER than a UTC-based one —
    // never narrower. Pinned as a property rather than a single instant: at 23:30 UTC on the last day of any
    // month the clinic has already turned the page, including across a year boundary.
    [Theory]
    [InlineData(2026, 12, 31, 2027, 1, 1)]   // New Year — the § 4.2 case, on the payment path
    [InlineData(2028, 2, 28, 2028, 2, 29)]   // leap February
    [InlineData(2026, 6, 30, 2026, 7, 1)]    // ordinary month end
    public void The_Clinic_Day_Has_Already_Turned_At_2330_Utc(
        int utcYear, int utcMonth, int utcDay, int localYear, int localMonth, int localDay)
    {
        var instant = new DateTime(utcYear, utcMonth, utcDay, 23, 30, 0, DateTimeKind.Utc);
        var clinicDate = new DateTime(localYear, localMonth, localDay, 0, 0, 0, DateTimeKind.Utc);

        Assert.Null(PaymentDateRules.Validate(clinicDate, Field, instant));
    }
}
