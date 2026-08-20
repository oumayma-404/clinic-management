using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// « Praticien » must name the practitioner the appointment was booked with.
///
/// <para>The defect these pin: <c>Appointment</c> carries <c>DoctorId</c> (a real FK, which the booking dialog
/// sends) and <c>DoctorName</c> (a free-text snapshot, which <b>no write path populates</b>) — and all three
/// appointment reads mapped the snapshot. On a real database 10 of 42 appointments had an id, 3 had a name, and
/// the two sets were disjoint, so every visit booked through the UI rendered « — ».</para>
///
/// <para>The first case is the one that was red. The rest exist because each is a way the naive fix breaks
/// something that was working: a hand-typed name with no id, a practitioner from another practice, and a
/// booking that genuinely names nobody.</para>
/// </summary>
public class AppointmentDoctorNameTests
{
    private static readonly Guid ClinicId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherClinicId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static Doctor Practitioner(Guid clinicId, string first, string last)
        => new(Guid.NewGuid(), clinicId, first, last, "Dentiste");

    private static async Task<IReadOnlyDictionary<Guid, string>> Roster(params Doctor[] doctors)
    {
        var repo = new Mock<IDoctorRepository>();
        repo.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doctors);
        return await AppointmentDoctorNames.ResolveRosterAsync(repo.Object, ClinicId);
    }

    // The reported bug: the id is stored, the snapshot is null, and the row must still name the practitioner.
    [Fact]
    public async Task An_Appointment_With_Only_A_DoctorId_Names_Its_Practitioner()
    {
        var doctor = Practitioner(ClinicId, "Khaireddine", "Hamdane");
        var roster = await Roster(doctor);

        Assert.Equal(
            "Khaireddine Hamdane",
            AppointmentDoctorNames.For(doctor.Id, storedName: null, roster));
    }

    // The live record wins: a practitioner who corrects the spelling of their own name sees it everywhere, which
    // a frozen snapshot cannot do.
    [Fact]
    public async Task The_Live_Name_Beats_A_Stale_Snapshot()
    {
        var doctor = Practitioner(ClinicId, "Khaireddine", "Hamdane");
        var roster = await Roster(doctor);

        Assert.Equal(
            "Khaireddine Hamdane",
            AppointmentDoctorNames.For(doctor.Id, storedName: "Dr K. Hamdan", roster));
    }

    // The snapshot is the only thing a row with no id has — a hand-typed name, or a seeded row. Dropping it
    // would blank the three appointments that were the ONLY ones displaying a praticien before this fix.
    [Fact]
    public async Task A_Stored_Name_With_No_DoctorId_Survives()
    {
        var roster = await Roster(Practitioner(ClinicId, "Salma", "Ben Youssef"));

        Assert.Equal(
            "Dr QA Auditeur",
            AppointmentDoctorNames.For(doctorId: null, storedName: "Dr QA Auditeur", roster));
    }

    // The roster is the caller's own clinic, so a foreign id resolves to nothing rather than leaking a name.
    // It falls through to the snapshot exactly like an unknown id, which is what makes the tenancy hole
    // unreachable rather than merely unlikely.
    [Fact]
    public async Task A_Practitioner_From_Another_Practice_Is_Not_Named()
    {
        var foreign = Practitioner(OtherClinicId, "Autre", "Cabinet");
        var roster = await Roster(Practitioner(ClinicId, "Salma", "Ben Youssef"));

        Assert.Null(AppointmentDoctorNames.For(foreign.Id, storedName: null, roster));
        Assert.Equal("saisi", AppointmentDoctorNames.For(foreign.Id, "saisi", roster));
    }

    // Null stays null: many bookings name no practitioner, and inventing « Praticien inconnu » would assert one.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_Booking_With_No_Practitioner_Names_Nobody(string? storedName)
    {
        var roster = await Roster(Practitioner(ClinicId, "Salma", "Ben Youssef"));

        Assert.Null(AppointmentDoctorNames.For(doctorId: null, storedName, roster));
    }

    // A practitioner whose name is blank must not shadow the snapshot with an empty string — the row would
    // render as « — » while the clinic can see the practitioner on the booking.
    [Fact]
    public async Task A_Nameless_Roster_Row_Does_Not_Blank_The_Snapshot()
    {
        var blank = Practitioner(ClinicId, " ", " ");
        var roster = await Roster(blank);

        Assert.Equal("Dr Untel", AppointmentDoctorNames.For(blank.Id, "Dr Untel", roster));
    }
}
