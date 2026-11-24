using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.Patients.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.UnitTests.Common;
using Moq;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// The patients page's « Date de création » filter: two Tunisian days, turned into instants by the server — never by
/// the browser, whose midnight is the workstation's. The matching itself is SQL (`PatientRepository`) and out of this
/// suite's reach; what is held here is the bounds and that they reach the repository verbatim.
/// </summary>
public class PatientCreatedWindowTests
{
    [Fact]
    public void Two_Days_Cover_Both_Whole_Tunisian_Days()
    {
        var window = PatientCreatedWindow.Resolve("2015-01-01", "2015-12-31", null, null);

        Assert.True(window.IsSuccess);
        // Tunis midnight is 23:00 UTC the evening before; the end is the last tick of the 31st in Tunis.
        Assert.Equal(new DateTime(2014, 12, 31, 23, 0, 0, DateTimeKind.Utc), window.Value.From);
        Assert.Equal(new DateTime(2015, 12, 31, 23, 0, 0, DateTimeKind.Utc).AddTicks(-1), window.Value.To);
    }

    [Fact]
    public void A_Patient_Imported_At_Noon_On_The_Last_Day_Is_Inside_The_Window()
    {
        // The import stores an old file's day at noon in Tunis — the filter must include that day.
        var stored = PatientRegistrationDate.InstantFor(new DateTime(2015, 12, 31), DateTime.UtcNow);
        var window = PatientCreatedWindow.Resolve("2015-12-31", "2015-12-31", null, null).Value;

        Assert.InRange(stored, window.From!.Value, window.To!.Value);
    }

    [Fact]
    public void Either_Bound_Alone_Is_A_Window()
    {
        var since = PatientCreatedWindow.Resolve("2020-01-01", null, null, null);
        var until = PatientCreatedWindow.Resolve(null, "2014-12-31", null, null);

        Assert.True(since.IsSuccess);
        Assert.NotNull(since.Value.From);
        Assert.Null(since.Value.To);
        Assert.True(until.IsSuccess);
        Assert.Null(until.Value.From);
        Assert.NotNull(until.Value.To);
    }

    [Fact]
    public void The_Dashboards_Instants_Still_Pass_Through_Untouched()
    {
        var from = new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 11, 30, 22, 59, 59, DateTimeKind.Utc);

        var window = PatientCreatedWindow.Resolve(null, null, from, to);

        Assert.Equal(from, window.Value.From);
        Assert.Equal(to, window.Value.To);
    }

    [Fact]
    public void A_Day_Wins_Over_Its_Instant_Twin()
    {
        var window = PatientCreatedWindow.Resolve("2015-01-01", null, new DateTime(2026, 1, 1), null);

        Assert.Equal(new DateTime(2014, 12, 31, 23, 0, 0, DateTimeKind.Utc), window.Value.From);
    }

    [Theory]
    [InlineData("01/01/2015", null)]
    [InlineData(null, "2015-13-40")]
    public void A_Malformed_Day_Is_Refused_Not_Ignored(string? fromDay, string? toDay)
    {
        var window = PatientCreatedWindow.Resolve(fromDay, toDay, null, null);

        Assert.True(window.IsFailure);
        Assert.Equal(PatientCreatedWindow.InvalidDay, window.Error);
    }

    [Fact]
    public void An_Inverted_Window_Is_Refused()
    {
        var window = PatientCreatedWindow.Resolve("2020-01-02", "2020-01-01", null, null);

        Assert.True(window.IsFailure);
        Assert.Equal(PatientCreatedWindow.Inverted, window.Error);
    }

    [Fact]
    public void The_List_Hands_The_Resolved_Bounds_To_The_Repository()
    {
        const string sub = "local|filter-test";
        var clinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var patients = new Mock<IPatientRepository>();
        var users = new Mock<IUserRepository>();
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(sub);
        users.Setup(r => r.GetByAuth0SubAsync(sub, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(sub, clinicId, "secretary", "staff@clinic.tn", "Staff"));
        patients.Setup(r => r.GetByClinicIdAsync(clinicId, It.IsAny<bool>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<PatientListSort>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Patient>().AsPage());

        var handler = new GetPatientsQueryHandler(patients.Object, users.Object, context.Object);
        var result = handler.Handle(
            new GetPatientsQuery { CreatedFromDay = "2015-01-01", CreatedToDay = "2015-12-31" }, CancellationToken.None).Result;

        Assert.True(result.IsSuccess, result.Error);
        var expected = PatientCreatedWindow.Resolve("2015-01-01", "2015-12-31", null, null).Value;
        patients.Verify(r => r.GetByClinicIdAsync(clinicId, It.IsAny<bool>(), expected.From, expected.To,
            It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<PatientListSort>(), It.IsAny<PageRequest?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void A_Malformed_Day_Refuses_The_List_Before_Any_Read()
    {
        const string sub = "local|filter-test";
        var patients = new Mock<IPatientRepository>(MockBehavior.Strict);
        var users = new Mock<IUserRepository>();
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(sub);
        users.Setup(r => r.GetByAuth0SubAsync(sub, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(sub, Guid.NewGuid(), "secretary", "staff@clinic.tn", "Staff"));

        var handler = new GetPatientsQueryHandler(patients.Object, users.Object, context.Object);
        var result = handler.Handle(new GetPatientsQuery { CreatedFromDay = "hier" }, CancellationToken.None).Result;

        Assert.True(result.IsFailure);
        Assert.Equal(PatientCreatedWindow.InvalidDay, result.Error);
    }
}
