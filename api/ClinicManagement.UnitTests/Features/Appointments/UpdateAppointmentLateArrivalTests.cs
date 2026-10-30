using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// The retard and « Absent → Terminé » through the update command (`features/post-visit-late-and-postpone`):
/// a tri-state field applied after the statut, a prompt that waits for the séance to really end, and a slot that is
/// re-checked when a visit comes back from « Absent » or « Annulé ».
/// </summary>
public class UpdateAppointmentLateArrivalTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid DoctorId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTime At = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<INotificationGenerator> _notifications = new();
    private readonly List<Appointment> _agenda = new();

    public UpdateAppointmentLateArrivalTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _appointments
            .Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _agenda.AsEnumerable());
    }

    private UpdateAppointmentCommandHandler CreateHandler() => new(
        _appointments.Object,
        new Mock<IProcedureTypeRepository>().Object,
        new Mock<IDoctorRepository>().Object,
        new Mock<IDoctorLeaveRepository>().Object,
        new Mock<IClinicRepository>().Object,
        new Mock<ITreatmentPlanRepository>().Object,
        _clinicResolver.Object,
        new Mock<IClinicContext>().Object,
        new Mock<IUnitOfWork>().Object,
        new Mock<IAppointmentGoogleSyncDispatcher>().Object,
        _notifications.Object,
        new Mock<IReminderScheduler>().Object,
        NullLogger<UpdateAppointmentCommandHandler>.Instance);

    private Appointment Visit(AppointmentStatus status, int? lateArrival = null)
    {
        var appointment = new Appointment(Guid.NewGuid(), ClinicId, PatientId, DoctorId, At, TimeSpan.FromMinutes(30));
        switch (status)
        {
            case AppointmentStatus.Scheduled: break;
            case AppointmentStatus.InProgress: appointment.Start(); break;
            case AppointmentStatus.AwaitingClosure: appointment.MarkAwaitingClosure(); break;
            case AppointmentStatus.Completed: appointment.Complete(); break;
            case AppointmentStatus.Cancelled: appointment.Cancel("motif"); break;
            case AppointmentStatus.NoShow: appointment.MarkAsNoShow(); break;
            default: throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (lateArrival is not null) appointment.RecordLateArrival(lateArrival);
        _appointments.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appointment);
        return appointment;
    }

    /// <summary>Somebody else booked into the same slot, with the same practitioner.</summary>
    private void SlotGivenAway() =>
        _agenda.Add(new Appointment(Guid.NewGuid(), ClinicId, Guid.NewGuid(), DoctorId, At, TimeSpan.FromMinutes(30)));

    [Fact]
    public async Task An_Ordinary_Save_That_Omits_The_Retard_Leaves_It_Alone()
    {
        var appointment = Visit(AppointmentStatus.InProgress, lateArrival: 10);

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Notes = "Rappeler pour le contrôle" }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(10, appointment.LateArrivalMinutes);
        Assert.Equal(10, result.Value!.LateArrivalMinutes);
    }

    [Fact]
    public async Task An_Explicit_Null_Clears_It()
    {
        var appointment = Visit(AppointmentStatus.Completed, lateArrival: 10);

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, LateArrivalMinutes = null }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(appointment.LateArrivalMinutes);
    }

    // The popup's « Noter un retard »: like its « Venu », it writes no statut.
    [Fact]
    public async Task A_Retard_On_Seance_Passee_Keeps_The_Statut()
    {
        var appointment = Visit(AppointmentStatus.AwaitingClosure);

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, LateArrivalMinutes = 15 }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AppointmentStatus.AwaitingClosure, appointment.Status);
        Assert.Equal(15, result.Value!.LateArrivalMinutes);
        Assert.Equal(At, appointment.AppointmentDateTime);
    }

    // « À clôturer » ⋯ « Venu en retard »: its « Venu » and the retard in one write — the retard applied last.
    [Fact]
    public async Task Venu_En_Retard_Lands_In_One_Write()
    {
        var appointment = Visit(AppointmentStatus.AwaitingClosure);

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Status = "Completed", LateArrivalMinutes = 15 },
            default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Equal(15, appointment.LateArrivalMinutes);
    }

    [Fact]
    public async Task Absent_With_A_Retard_Is_Refused_As_A_Contradiction()
    {
        var appointment = Visit(AppointmentStatus.AwaitingClosure);

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Status = "NoShow", LateArrivalMinutes = 15 }, default);

        Assert.False(result.IsSuccess);
        Assert.Contains("retard", result.Error);
    }

    [Fact]
    public async Task A_Retard_Before_The_Visit_Has_Started_Is_Refused()
    {
        var appointment = Visit(AppointmentStatus.Scheduled);

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, LateArrivalMinutes = 15 }, default);

        Assert.False(result.IsSuccess);
        Assert.Null(appointment.LateArrivalMinutes);
    }

    // Noted while the patient is in the chair: the prompt waits for booked end + retard, so it comes once.
    [Fact]
    public async Task A_Retard_Noted_During_The_Seance_Pushes_The_Prompt_Back()
    {
        var appointment = Visit(AppointmentStatus.InProgress);

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, LateArrivalMinutes = 15 }, default);

        Assert.True(result.IsSuccess, result.Error);
        _notifications.Verify(g => g.EnsurePostVisitReviewAsync(
            ClinicId, appointment.Id, DoctorId, It.IsAny<string>(), At.AddMinutes(45), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Noted from the popup itself: the prompt is already up and must not be moved later — that is the second popup.
    [Fact]
    public async Task A_Retard_Noted_After_The_Slot_Does_Not_Move_The_Prompt()
    {
        var appointment = Visit(AppointmentStatus.AwaitingClosure);

        await CreateHandler().Handle(new UpdateAppointmentCommand { Id = appointment.Id, LateArrivalMinutes = 15 }, default);

        _notifications.Verify(g => g.EnsurePostVisitReviewAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Finalement_Venu_On_A_Free_Slot_Goes_Through()
    {
        var appointment = Visit(AppointmentStatus.NoShow);

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Status = "Completed", LateArrivalMinutes = 40 },
            default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Equal(40, appointment.LateArrivalMinutes);
    }

    // Absent frees the slot; « Terminé » takes it back, so the clash is named rather than met as the database's 409.
    [Fact]
    public async Task Finalement_Venu_Into_A_Slot_Given_Away_Names_The_Clash()
    {
        var appointment = Visit(AppointmentStatus.NoShow);
        SlotGivenAway();

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Status = "Completed" }, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppointmentScheduling.SlotTakenCode, result.Code);
    }

    [Fact]
    public async Task Finalement_Venu_With_The_Override_Records_The_Overlap()
    {
        var appointment = Visit(AppointmentStatus.NoShow);
        SlotGivenAway();

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Status = "Completed", AllowOverlap = true }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.True(appointment.BookedWithOverlap);
    }

    [Fact]
    public async Task Reactivating_Into_A_Slot_Given_Away_Names_The_Clash_Too()
    {
        var appointment = Visit(AppointmentStatus.Cancelled);
        SlotGivenAway();

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Status = "Scheduled" }, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppointmentScheduling.SlotTakenCode, result.Code);
    }

    [Fact]
    public void A_Retard_Keeps_The_Visit_Off_A_Cloturer_Until_The_Seance_Has_Really_Ended()
    {
        var visit = new VisitClosureInput(
            AppointmentId: Guid.NewGuid(), PatientId: PatientId, Status: AppointmentStatus.InProgress, StartUtc: At,
            Duration: TimeSpan.FromMinutes(30), HasFiche: false, FicheCost: null, HasLiveInvoice: false,
            CoveredByPlan: false, NothingToBill: false, LateArrivalMinutes: 15);

        Assert.False(VisitClosureRules.IsClosable(visit, At.AddMinutes(30)));
        Assert.True(VisitClosureRules.IsClosable(visit, At.AddMinutes(45)));
    }

    /// <summary>Four sites build an <c>AppointmentDto</c> by hand; the one that forgets the retard hides it on that screen.</summary>
    [Fact]
    public void Every_AppointmentDto_Projection_Carries_The_Retard()
    {
        // Red proof: the check must fail on a projection without the field.
        Assert.False(CarriesTheRetard("new AppointmentDto { Id = a.Id, Status = s }", 0));

        var root = Path.Combine(SolutionRoot(), "ClinicManagement.Application");
        var sites = 0;
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/obj/") || relative.Contains("/bin/")) continue;

            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in Regex.Matches(text, @"new AppointmentDto\s*\{"))
            {
                sites++;
                if (!CarriesTheRetard(text, match.Index)) missing.Add(relative);
            }
        }

        Assert.True(sites >= 4, $"Only {sites} projections found — this guard would pass while checking nothing.");
        Assert.Empty(missing);
    }

    private static bool CarriesTheRetard(string text, int start)
    {
        var open = text.IndexOf('{', start);
        var close = open < 0 ? -1 : text.IndexOf('}', open);
        return close > open && text[open..close].Contains("LateArrivalMinutes =");
    }

    private static string SolutionRoot([CallerFilePath] string thisFile = "")
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ClinicManagement.sln"))) return dir.FullName;
        }

        // Thrown, never skipped: a derived guard that scans nothing reports green over everything.
        throw new InvalidOperationException($"ClinicManagement.sln not found walking up from {thisFile}.");
    }
}
