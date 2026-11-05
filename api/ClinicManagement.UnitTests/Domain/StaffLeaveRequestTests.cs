using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// A secretary's congé request: what it refuses on arrival, the days an admin may count, and the one-way walk from
/// « en attente » to accepted, refused or withdrawn.
/// </summary>
public class StaffLeaveRequestTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateOnly Nov16 = new(2026, 11, 16);
    private static readonly DateOnly Nov18 = new(2026, 11, 18);
    private const string Secretary = "local|secretary";
    private const string Admin = "local|admin";

    private static StaffLeaveRequest Pending(DateOnly? start = null, DateOnly? end = null) =>
        new(Guid.NewGuid(), ClinicId, Secretary, StaffLeaveType.PaidLeave, start ?? Nov16, end ?? Nov18, false, "Mariage", 3m);

    public static TheoryData<DateOnly, DateOnly, bool, string?, string> Refusals => new()
    {
        { default, Nov18, false, "Mariage", "Les dates du congé sont requises." },
        { Nov18, Nov16, false, "Mariage", "Le dernier jour doit être le même jour ou après le premier." },
        { Nov16, Nov16.AddDays(366), false, "Mariage", "Un congé ne peut pas dépasser 366 jours — vérifiez l'année saisie." },
        { Nov16, Nov18, true, "Mariage", "Une demi-journée ne peut porter que sur un seul jour." },
        { Nov16, Nov18, false, "   ", "Le motif est requis." },
        { Nov16, Nov18, false, new string('x', 501), "Le motif ne peut pas dépasser 500 caractères." },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void A_Request_Is_Refused_With_Its_French_Reason(DateOnly start, DateOnly end, bool halfDay, string? reason, string expected)
    {
        Assert.Equal(expected, StaffLeaveRequest.RefusalFor(start, end, halfDay, reason));
    }

    [Fact]
    public void A_Year_Exactly_And_A_Half_Day_On_One_Day_Are_Accepted()
    {
        Assert.Null(StaffLeaveRequest.RefusalFor(Nov16, Nov16.AddDays(365), false, "Long congé"));
        Assert.Null(StaffLeaveRequest.RefusalFor(Nov16, Nov16, true, "Rendez-vous"));
        Assert.Null(StaffLeaveRequest.RefusalFor(Nov16, Nov18, false, new string('x', 500)));
    }

    [Fact]
    public void A_New_Request_Waits_With_Its_Motif_Trimmed()
    {
        var leave = new StaffLeaveRequest(Guid.NewGuid(), ClinicId, Secretary, StaffLeaveType.Sick, Nov16, Nov16, true, "  Médecin  ", 0.5m);

        Assert.Equal(StaffLeaveStatus.Pending, leave.Status);
        Assert.Equal("Médecin", leave.Reason);
        Assert.Equal(0.5m, leave.RequestedDays);
        Assert.Null(leave.CountedDays);
        Assert.False(leave.EnteredByAdmin);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(0.25)]
    [InlineData(367)]
    public void Counted_Days_Outside_Half_Days_Between_0_And_366_Are_Refused(double days)
    {
        Assert.NotNull(StaffLeaveRequest.CountRefusalFor((decimal)days));
        Assert.Throws<ArgumentException>(() => Pending().Approve(Admin, (decimal)days));
    }

    [Fact]
    public void Approving_Stores_The_Counted_Days_And_Who_Decided()
    {
        var leave = Pending();

        leave.Approve(Admin, 2.5m);

        Assert.Equal(StaffLeaveStatus.Approved, leave.Status);
        Assert.Equal(2.5m, leave.CountedDays);
        Assert.Equal(3m, leave.RequestedDays);
        Assert.Equal(Admin, leave.DecidedByUserId);
        Assert.NotNull(leave.DecidedAt);
    }

    [Fact]
    public void Refusing_Requires_A_Motif_And_Keeps_It_Trimmed()
    {
        var leave = Pending();
        Assert.Throws<ArgumentException>(() => leave.Reject(Admin, " "));
        Assert.Equal(StaffLeaveStatus.Pending, leave.Status);

        leave.Reject(Admin, "  Période chargée ");

        Assert.Equal(StaffLeaveStatus.Rejected, leave.Status);
        Assert.Equal("Période chargée", leave.RejectionReason);
    }

    [Fact]
    public void A_Decided_Request_Cannot_Be_Decided_Again()
    {
        var approved = Pending();
        approved.Approve(Admin, 3m);
        var rejected = Pending();
        rejected.Reject(Admin, "Non");

        Assert.Throws<InvalidOperationException>(() => approved.Reject(Admin, "Non"));
        Assert.Throws<InvalidOperationException>(() => approved.Approve(Admin, 3m));
        Assert.Throws<InvalidOperationException>(() => rejected.Approve(Admin, 3m));
    }

    [Fact]
    public void A_Waiting_Request_Can_Be_Withdrawn_On_Any_Day()
    {
        var leave = Pending();

        Assert.True(leave.CanBeCancelledOn(Nov18.AddDays(30)));
        leave.Cancel(Secretary, Nov18.AddDays(30));

        Assert.Equal(StaffLeaveStatus.Cancelled, leave.Status);
        Assert.Equal(Secretary, leave.CancelledByUserId);
        Assert.False(leave.HoldsItsDays);
    }

    [Fact]
    public void An_Accepted_Congé_Can_Be_Withdrawn_Only_Before_Its_First_Day()
    {
        var leave = Pending();
        leave.Approve(Admin, 3m);

        Assert.True(leave.CanBeCancelledOn(Nov16.AddDays(-1)));
        Assert.False(leave.CanBeCancelledOn(Nov16));
        Assert.False(leave.CanBeCancelledOn(Nov18.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => leave.Cancel(Secretary, Nov16));
        Assert.Equal(StaffLeaveStatus.Approved, leave.Status);
    }

    [Fact]
    public void Refused_And_Withdrawn_Requests_Are_Final()
    {
        var rejected = Pending();
        rejected.Reject(Admin, "Non");
        var cancelled = Pending();
        cancelled.Cancel(Secretary, Nov16.AddDays(-5));

        Assert.False(rejected.CanBeCancelledOn(Nov16.AddDays(-5)));
        Assert.False(rejected.CanMoveTo(StaffLeaveStatus.Approved));
        Assert.False(cancelled.CanMoveTo(StaffLeaveStatus.Approved));
        Assert.False(cancelled.CanMoveTo(StaffLeaveStatus.Pending));
    }

    [Fact]
    public void An_Absence_Recorded_By_The_Admin_Is_Accepted_At_Once()
    {
        var leave = StaffLeaveRequest.RecordByAdmin(
            Guid.NewGuid(), ClinicId, Secretary, StaffLeaveType.Unpaid, Nov16, Nov18, false, "Absence", 3m, Admin);

        Assert.Equal(StaffLeaveStatus.Approved, leave.Status);
        Assert.True(leave.EnteredByAdmin);
        Assert.Equal(3m, leave.CountedDays);
        Assert.Equal(Admin, leave.DecidedByUserId);
    }

    [Fact]
    public void An_Absence_The_Admin_Recorded_Is_Withdrawn_By_An_Admin_Only()
    {
        var absence = StaffLeaveRequest.RecordByAdmin(
            Guid.NewGuid(), ClinicId, Secretary, StaffLeaveType.Unpaid, Nov16, Nov18, false, "Absence", 3m, Admin);
        var asked = Pending();
        asked.Approve(Admin, 3m);
        var before = Nov16.AddDays(-1);

        Assert.False(absence.CanBeCancelledBy(byAdmin: false, before));
        Assert.True(absence.CanBeCancelledBy(byAdmin: true, before));
        Assert.True(asked.CanBeCancelledBy(byAdmin: false, before));
    }

    [Theory]
    [InlineData(null, "Indiquez le motif du refus.")]
    [InlineData("  ", "Indiquez le motif du refus.")]
    [InlineData("Période chargée", null)]
    public void A_Refusal_Motif_Is_Required(string? motif, string? expected)
    {
        Assert.Equal(expected, StaffLeaveRequest.RejectionRefusalFor(motif));
    }

    [Fact]
    public void A_Congé_Notification_Takes_Only_A_Congé_Category()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StaffNotification.ForStaffLeave(
            Guid.NewGuid(), ClinicId, NotificationCategory.AppointmentCreated, "Titre", "Message", Admin, Secretary));
    }

    [Theory]
    [InlineData(24.5, null)]
    [InlineData(-1, "Le droit annuel se compte par demi-journée, entre 0 et 366 jours.")]
    [InlineData(24.25, "Le droit annuel se compte par demi-journée, entre 0 et 366 jours.")]
    [InlineData(367, "Le droit annuel se compte par demi-journée, entre 0 et 366 jours.")]
    public void A_Yearly_Allowance_Is_Counted_In_Half_Days_Up_To_A_Year(double days, string? expected)
    {
        Assert.Equal(expected, User.AllowanceRefusalFor((decimal)days));
        Assert.Null(User.AllowanceRefusalFor(null));
    }
}
