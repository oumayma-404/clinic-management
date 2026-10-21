using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.DoctorLeaves.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.DoctorLeaves;

/// <summary>The two congé reads: the agenda's window across the cabinet, and one practitioner's list.</summary>
public class DoctorLeaveQueryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateOnly Nov1 = new(2026, 11, 1);

    private readonly Mock<IDoctorLeaveRepository> _leaves = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<ICurrentClinicResolver> _clinic = new();

    public DoctorLeaveQueryTests()
    {
        _clinic.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<Guid>.Success(ClinicId));
    }

    private GetClinicDoctorLeavesQueryHandler ClinicRead() => new(_leaves.Object, _doctors.Object, _clinic.Object);
    private GetDoctorLeavesQueryHandler DoctorRead() => new(_leaves.Object, _doctors.Object, _clinic.Object);

    private static Doctor Practitioner(Guid clinicId, string firstName) => new(Guid.NewGuid(), clinicId, firstName, "Trabelsi", "General");

    [Fact]
    public async Task The_Agenda_Read_Names_Each_Practitioner_From_The_Roster_Retired_Ones_Included()
    {
        var nadia = Practitioner(ClinicId, "Nadia");
        var retired = Practitioner(ClinicId, "Ali");
        retired.Retire();
        _doctors.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { nadia, retired });
        _leaves.Setup(r => r.GetOverlappingAsync(ClinicId, Nov1, Nov1.AddDays(41), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new DoctorLeave(Guid.NewGuid(), ClinicId, nadia.Id, Nov1, Nov1.AddDays(2), null),
                new DoctorLeave(Guid.NewGuid(), ClinicId, retired.Id, Nov1, Nov1, null),
            });

        var result = await ClinicRead().Handle(new GetClinicDoctorLeavesQuery { FromDay = Nov1, ToDay = Nov1.AddDays(41) }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { "Nadia Trabelsi", "Ali Trabelsi" }, result.Value!.Select(l => l.DoctorName));
    }

    [Fact]
    public async Task The_Agenda_Read_Refuses_A_Window_Over_400_Days_Or_Backwards()
    {
        _leaves.Setup(r => r.GetOverlappingAsync(ClinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DoctorLeave>());

        var tooLong = await ClinicRead().Handle(new GetClinicDoctorLeavesQuery { FromDay = Nov1, ToDay = Nov1.AddDays(400) }, default);
        var backwards = await ClinicRead().Handle(new GetClinicDoctorLeavesQuery { FromDay = Nov1, ToDay = Nov1.AddDays(-1) }, default);
        var longest = await ClinicRead().Handle(new GetClinicDoctorLeavesQuery { FromDay = Nov1, ToDay = Nov1.AddDays(399) }, default);

        Assert.Equal("La période demandée dépasse 400 jours.", tooLong.Error);
        Assert.True(backwards.IsFailure);
        Assert.True(longest.IsSuccess, longest.Error);
    }

    [Fact]
    public async Task A_Practitioners_List_Is_Read_From_Their_Own_Clinic_Only()
    {
        var foreign = Practitioner(OtherClinicId, "Karim");
        _doctors.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await DoctorRead().Handle(new GetDoctorLeavesQuery { DoctorId = foreign.Id }, default);

        Assert.Equal("Praticien introuvable.", result.Error);
        _leaves.Verify(r => r.GetByDoctorAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
