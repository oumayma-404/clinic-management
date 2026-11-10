using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Team;

/// <summary>
/// What the bell and the e-mail say about a congé: one date style (« du 16 au 18 novembre 2026 »), which half of a demi-journée,
/// days withdrawn only for a congé payé, a balance below zero said as « dépassement », and the admin's motif quoted.
/// </summary>
public class StaffLeaveNoticesTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string Secretary = "local|secretary";
    private const string Admin = "local|admin";

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static StaffLeaveRequest Leave(
        DateOnly start, DateOnly end, StaffLeaveType type = StaffLeaveType.PaidLeave, bool halfDay = false,
        StaffLeaveHalfDayPart? part = null, decimal days = 3m) =>
        new(Guid.NewGuid(), ClinicId, Secretary, type, start, end, halfDay, "Motif", days, part);

    [Theory]
    [InlineData(2026, 11, 16, 2026, 11, 16, "le 16 novembre 2026")]
    [InlineData(2026, 11, 16, 2026, 11, 18, "du 16 au 18 novembre 2026")]
    [InlineData(2026, 11, 30, 2026, 12, 2, "du 30 novembre au 2 décembre 2026")]
    [InlineData(2026, 12, 30, 2027, 1, 2, "du 30 décembre 2026 au 2 janvier 2027")]
    [InlineData(2026, 12, 1, 2026, 12, 4, "du 1er au 4 décembre 2026")]
    [InlineData(2026, 7, 1, 2026, 7, 1, "le 1er juillet 2026")]
    public void A_Period_Reads_In_Words(int y1, int m1, int d1, int y2, int m2, int d2, string expected)
    {
        Assert.Equal(expected, FrenchDayPeriod.Words(D(y1, m1, d1), D(y2, m2, d2)));
    }

    [Theory]
    [InlineData(3, "3 jours")]
    [InlineData(1, "1 jour")]
    [InlineData(0.5, "0,5 jour")]
    [InlineData(-3, "−3 jours")]
    [InlineData(-1, "−1 jour")]
    public void A_Count_Takes_Its_Plural_From_Its_Size_And_A_True_Minus(double days, string expected)
    {
        Assert.Equal(expected, FrenchDayPeriod.Days((decimal)days));
    }

    [Theory]
    [InlineData(9, "9 jours")]
    [InlineData(0, "0 jour")]
    [InlineData(-3, "3 jours de dépassement")]
    [InlineData(-0.5, "0,5 jour de dépassement")]
    public void A_Balance_Below_Zero_Is_Said_As_An_Overrun(double days, string expected)
    {
        Assert.Equal(expected, FrenchDayPeriod.Balance((decimal)days));
    }

    [Theory]
    [InlineData(StaffLeaveHalfDayPart.Morning, "le 16 novembre 2026 matin")]
    [InlineData(StaffLeaveHalfDayPart.Afternoon, "le 16 novembre 2026 après-midi")]
    [InlineData(null, "le 16 novembre 2026 (demi-journée)")]
    public void A_Half_Day_Says_Which_Half(StaffLeaveHalfDayPart? part, string expected)
    {
        Assert.Equal(expected, StaffLeaveNotices.Period(Leave(D(2026, 11, 16), D(2026, 11, 16), halfDay: true, part: part, days: 0.5m)));
    }

    [Fact]
    public void An_Accepted_Congé_Payé_Names_The_Days_Taken_Off_Her_Balance()
    {
        var leave = Leave(D(2026, 11, 16), D(2026, 11, 18));
        leave.Approve(Admin, 2.5m);

        Assert.Equal("Votre congé du 16 au 18 novembre 2026 est accordé · 2,5 j retirés de votre solde.", StaffLeaveNotices.Approved(leave).Message);
    }

    [Theory]
    [InlineData(StaffLeaveType.Sick)]
    [InlineData(StaffLeaveType.Unpaid)]
    [InlineData(StaffLeaveType.Other)]
    public void An_Accepted_Absence_That_Is_Not_A_Congé_Payé_Withdraws_Nothing(StaffLeaveType type)
    {
        var leave = Leave(D(2026, 11, 16), D(2026, 11, 18), type);
        leave.Approve(Admin, 3m);

        Assert.Equal("Votre congé du 16 au 18 novembre 2026 est accordé.", StaffLeaveNotices.Approved(leave).Message);
        Assert.DoesNotContain(
            StaffLeaveNotices.Email(leave, "Cabinet", "Ines", "Dr Salma", null, null).Details,
            d => d.Label == "Retirés de votre solde");
    }

    [Fact]
    public void One_Day_Withdrawn_Is_Singular()
    {
        var leave = Leave(D(2026, 11, 16), D(2026, 11, 16), days: 1m);
        leave.Approve(Admin, 1m);

        Assert.EndsWith("1 j retiré de votre solde.", StaffLeaveNotices.Approved(leave).Message);
    }

    [Fact]
    public void The_Decision_Mail_Says_An_Overrun_Rather_Than_A_Negative_Balance()
    {
        var leave = Leave(D(2026, 11, 16), D(2026, 11, 18));
        leave.Approve(Admin, 3m);

        var remaining = StaffLeaveNotices.Email(leave, "Cabinet", "Ines", "Dr Salma", -3m, null).Details
            .Single(d => d.Label == "Congé payé restant en 2026");

        Assert.Equal("3 jours de dépassement", remaining.Value);
    }

    [Fact]
    public void An_Admins_Withdrawal_Quotes_The_Motif()
    {
        var accepted = Leave(D(2026, 11, 16), D(2026, 11, 18));
        accepted.Approve(Admin, 3m);
        accepted.Cancel(Admin, D(2026, 11, 17), byAdmin: true, reason: "Fermeture");

        var (title, message) = StaffLeaveNotices.CancelledByAdmin("Dr Salma", accepted, wasApproved: true);

        Assert.Equal("Congé annulé", title);
        Assert.Equal("Votre congé du 16 au 18 novembre 2026 a été annulé par Dr Salma : « Fermeture ».", message);
    }
}
