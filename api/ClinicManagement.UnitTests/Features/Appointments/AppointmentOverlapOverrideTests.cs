using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// A double-booking is <b>advisory, not a prohibition</b>.
///
/// <para>The overlap rule used to be enforced three times over — a disabled Save button, an application refusal, and
/// a PostgreSQL exclusion constraint — so a clinic that genuinely double-books (a second chair, an assistant
/// preparing one patient while the dentist starts another, an emergency squeezed into a taken slot) could not record
/// the day it actually had. The refusal now carries <see cref="AppointmentScheduling.SlotTakenCode"/> so the client
/// can offer « Continuer quand même », exactly as the working-hours rule already did, and the acceptance is
/// <b>recorded</b> on the appointment rather than silently allowed.</para>
///
/// <para>⚠️ The recorded flag is not cosmetic: it is a term in the exclusion constraint's predicate
/// (<c>AllowAcknowledgedOverlap</c>), so it is the thing that makes the write possible at all. Setting it when there
/// was no collision would quietly exempt ordinary bookings from the database's protection — which is why
/// <see cref="Does_Not_Mark_An_Overlap_When_There_Was_No_Collision"/> exists.</para>
/// </summary>
public class AppointmentOverlapOverrideTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid DoctorId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly DateTime At = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IProcedureTypeRepository> _procedureTypes = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private const string Auth0Sub = "auth0|booking-user";

    public AppointmentOverlapOverrideTests()
    {
        // This handler still uses the legacy clinic-resolution idiom (IClinicContext + IUserRepository) rather than
        // ICurrentClinicResolver, so the harness resolves the clinic the same way it does.
        _clinicContext.Setup(c => c.GetUserId()).Returns(Auth0Sub);
        _users.Setup(r => r.GetByAuth0SubAsync(Auth0Sub, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(Auth0Sub, ClinicId, "doctor"));
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(PatientId, ClinicId, "Jean", "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M"));
        // A real practitioner in the clinic — the handler validates DoctorId before it ever reaches the collision
        // check, so a null doctor fails at « Praticien introuvable » and never exercises this class's subject.
        // The doctor has NO working hours of its own and the clinic returns none either, so
        // CheckWorkingHoursAsync is unrestricted (R-12) and these tests isolate the collision rule.
        _doctors.Setup(r => r.GetByIdAsync(DoctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Doctor(DoctorId, ClinicId, "Khaireddine", "Hamdane", "Periodontics"));
        _clinics.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Clinic?)null);
        _appointments.Setup(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment a, CancellationToken _) => a);
    }

    private CreateAppointmentCommandHandler CreateHandler() => new(
        _appointments.Object,
        _patients.Object,
        _doctors.Object,
        _clinics.Object,
        _procedureTypes.Object,
        new Mock<ITreatmentPlanRepository>().Object,
        _users.Object,
        _clinicContext.Object,
        _uow.Object,
        new Mock<INotificationGenerator>().Object,
        new Mock<IReminderScheduler>().Object,
        new Mock<IAppointmentGoogleSyncDispatcher>().Object,
        NullLogger<CreateAppointmentCommandHandler>.Instance);

    /// <summary>An existing 30-minute booking for the same practitioner at the same time — the collision.</summary>
    private void ExistingBookingAt(DateTime at)
    {
        var existing = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, DoctorId, at, TimeSpan.FromMinutes(30));
        _appointments
            .Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { existing });
    }

    private void NoExistingBookings() =>
        _appointments
            .Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>());

    private static CreateAppointmentCommand Command(bool allowOverlap = false) => new()
    {
        PatientId = PatientId,
        DoctorId = DoctorId,
        AppointmentDateTime = At,
        DurationMinutes = 30,
        AllowOverlap = allowOverlap,
    };

    // Without the override the refusal stands — the guard is not simply gone.
    [Fact]
    public async Task Refuses_A_Collision_When_The_Override_Is_Not_Given()
    {
        ExistingBookingAt(At);

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("déjà réservé", result.Error);
    }

    // The refusal must carry the machine-readable code, or the dialog cannot tell a double-booking apart from any
    // other 400 and the « Continuer quand même » path is unreachable. This is the contract with the frontend.
    [Fact]
    public async Task The_Refusal_Carries_The_SlotTaken_Code()
    {
        ExistingBookingAt(At);

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        Assert.Equal(AppointmentScheduling.SlotTakenCode, result.Code);
        // Distinct from the working-hours code — the dialog branches on them separately.
        Assert.NotEqual(AppointmentScheduling.OutsideWorkingHoursCode, result.Code);
    }

    // The point of the change: with the override the booking succeeds.
    [Fact]
    public async Task Books_The_Overlapping_Appointment_When_The_Override_Is_Given()
    {
        ExistingBookingAt(At);

        var result = await CreateHandler().Handle(Command(allowOverlap: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // And it is recorded as deliberate. Without this flag the row is not exempt from the exclusion constraint, so
    // the save would fail at the database even though the application allowed it.
    [Fact]
    public async Task Records_The_Acknowledgement_On_The_Appointment()
    {
        ExistingBookingAt(At);
        Appointment? saved = null;
        _appointments.Setup(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment a, CancellationToken _) => { saved = a; return a; });

        await CreateHandler().Handle(Command(allowOverlap: true), CancellationToken.None);

        Assert.NotNull(saved);
        Assert.True(saved!.BookedWithOverlap);
    }

    // The guard that keeps the database protection meaningful: passing the flag on a booking that does NOT collide
    // must not mark it. If it did, every appointment created through a client that always sends the flag would be
    // exempt from the exclusion constraint, and accidental double-booking would be possible again.
    [Fact]
    public async Task Does_Not_Mark_An_Overlap_When_There_Was_No_Collision()
    {
        NoExistingBookings();
        Appointment? saved = null;
        _appointments.Setup(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment a, CancellationToken _) => { saved = a; return a; });

        var result = await CreateHandler().Handle(Command(allowOverlap: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.False(saved!.BookedWithOverlap);
    }

    // ---- the domain marker itself ----------------------------------------------------------------------

    // Independent of the flag: an acknowledged overlap can be withdrawn, so a booking moved to a free slot does not
    // keep its constraint exemption forever.
    [Fact]
    public void The_Acknowledgement_Can_Be_Set_And_Cleared()
    {
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, DoctorId, At, TimeSpan.FromMinutes(30));
        Assert.False(appointment.BookedWithOverlap);

        appointment.MarkBookedWithOverlap();
        Assert.True(appointment.BookedWithOverlap);

        appointment.ClearOverlapAcknowledgement();
        Assert.False(appointment.BookedWithOverlap);
    }

    // Clearing an appointment that never had the flag is a no-op rather than a spurious UpdatedAt bump.
    [Fact]
    public void Clearing_An_Unacknowledged_Appointment_Is_A_No_Op()
    {
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, DoctorId, At, TimeSpan.FromMinutes(30));
        var before = appointment.UpdatedAt;

        appointment.ClearOverlapAcknowledgement();

        Assert.False(appointment.BookedWithOverlap);
        Assert.Equal(before, appointment.UpdatedAt);
    }

    // The overlap predicate is half-open [start, end): a booking that starts exactly when another ends does NOT
    // collide. Pinned here because it is the difference between back-to-back appointments working and every 09:30
    // booking being refused by a 09:00–09:30 one.
    [Theory]
    [InlineData(0, true)]    // same start — overlaps
    [InlineData(15, true)]   // starts inside — overlaps
    [InlineData(30, false)]  // starts exactly at the other's end — back-to-back, no overlap
    [InlineData(45, false)]  // clear of it
    public void Overlap_Is_Half_Open(int offsetMinutes, bool expected)
    {
        var overlaps = AppointmentScheduling.Overlaps(
            At, TimeSpan.FromMinutes(30),
            At.AddMinutes(offsetMinutes), TimeSpan.FromMinutes(30));

        Assert.Equal(expected, overlaps);
    }
}
