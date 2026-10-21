using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary><see cref="DoctorLeave"/> — whole clinic-local days, both ends inclusive, at most 366 days.</summary>
public class DoctorLeaveTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DoctorId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateOnly Aug12 = new(2026, 8, 12);
    private static readonly DateOnly Aug20 = new(2026, 8, 20);

    private static DoctorLeave Leave(DateOnly start, DateOnly end, string? note = null) =>
        new(Guid.NewGuid(), ClinicId, DoctorId, start, end, note);

    [Fact]
    public void A_Single_Day_Congé_Counts_One_Day()
    {
        Assert.Equal(1, Leave(Aug12, Aug12).DayCount);
        Assert.Equal(9, Leave(Aug12, Aug20).DayCount);
    }

    [Fact]
    public void An_End_Before_The_Start_Is_Refused()
    {
        Assert.NotNull(DoctorLeave.RefusalFor(Aug20, Aug12, null));
        Assert.Throws<ArgumentException>(() => Leave(Aug20, Aug12));
    }

    [Fact]
    public void Exactly_366_Days_Is_Accepted_And_367_Is_Refused()
    {
        var start = new DateOnly(2027, 1, 1);

        Assert.Null(DoctorLeave.RefusalFor(start, start.AddDays(DoctorLeave.MaxDays - 1), null));
        var refusal = DoctorLeave.RefusalFor(start, start.AddDays(DoctorLeave.MaxDays), null);

        Assert.Equal("Un congé ne peut pas dépasser 366 jours — vérifiez l'année saisie.", refusal);
    }

    [Fact]
    public void A_Motif_Of_200_Characters_Is_Accepted_And_201_Is_Refused()
    {
        Assert.Null(DoctorLeave.RefusalFor(Aug12, Aug20, new string('a', DoctorLeave.MaxNoteLength)));
        Assert.NotNull(DoctorLeave.RefusalFor(Aug12, Aug20, new string('a', DoctorLeave.MaxNoteLength + 1)));
    }

    [Fact]
    public void A_Motif_Is_Trimmed_And_A_Blank_One_Is_Stored_As_None()
    {
        Assert.Equal("Vacances", Leave(Aug12, Aug20, "  Vacances ").Note);
        Assert.Null(Leave(Aug12, Aug20, "   ").Note);
    }

    [Fact]
    public void Missing_Dates_Are_Refused_Rather_Than_Stored_As_Year_One()
    {
        Assert.Equal("Les dates du congé sont requises.", DoctorLeave.RefusalFor(default, default, null));
        Assert.NotNull(DoctorLeave.RefusalFor(Aug12, default, null));
    }

    [Fact]
    public void Change_Moves_The_Period_And_Refuses_An_Invalid_One()
    {
        var leave = Leave(Aug12, Aug20, "Vacances");

        leave.Change(Aug12, Aug12.AddDays(2), null);

        Assert.Equal(Aug12.AddDays(2), leave.EndDate);
        Assert.Null(leave.Note);
        Assert.NotNull(leave.UpdatedAt);
        Assert.Throws<ArgumentException>(() => leave.Change(Aug20, Aug12, null));
        Assert.Equal(Aug12.AddDays(2), leave.EndDate);
    }
}
