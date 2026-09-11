using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// « Séance passée » is written by <c>AppointmentProgressJob</c> alone, and a request for it must be
/// <b>refused</b> — never silently accepted.
///
/// <para>
/// THE defect, measured in production 2026-09-11. A visit was dragged onto a past slot, the job started it
/// (« En cours »), and the user then dragged it so its slot had ended and picked « Séance passée » from the
/// status control to correct it. Twice, eleven seconds apart. Both requests returned <b>HTTP 200 with no
/// error and the status unchanged</b>, and the audit ledger recorded two entries with an <i>empty</i>
/// <c>ChangedFields</c> — two writes that changed nothing.
/// </para>
/// <para>
/// Two independent faults, one symptom. (1) <c>AppointmentDto.AllowedNextStatuses</c> was projected straight
/// from <c>Appointment.NextStatusesFrom</c> at four sites, so the dropdown offered « Séance passée » from
/// <c>Scheduled</c>, <c>Confirmed</c> and <c>InProgress</c> — the frontend had decided against exactly that in
/// as many words (`MANUALLY_SETTABLE_STATUSES`) but applied the filter only on the fallback branch taken when
/// the server sends no list. (2) The handler's transition <c>switch</c> had no arm for the status and no
/// <c>default</c>, so a guard-passing request fell through to the save untouched.
/// </para>
/// <para>
/// ⚠️ The job's own pass was never broken and is not what this file changes: the audit shows it moved the same
/// appointment `InProgress → AwaitingClosure` 48 seconds after the drag. What made that look broken was the two
/// silent refusals landing inside that window.
/// </para>
/// </summary>
public class AppointmentManualStatusTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime At = new(2026, 9, 11, 18, 15, 0, DateTimeKind.Utc);

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public AppointmentManualStatusTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
    }

    private UpdateAppointmentCommandHandler CreateHandler() => new(
        _appointments.Object,
        new Mock<IProcedureTypeRepository>().Object,
        new Mock<IDoctorRepository>().Object,
        new Mock<IClinicRepository>().Object,
        new Mock<ITreatmentPlanRepository>().Object,
        _clinicResolver.Object,
        new Mock<IClinicContext>().Object,
        _uow.Object,
        new Mock<IAppointmentGoogleSyncDispatcher>().Object,
        new Mock<INotificationGenerator>().Object,
        new Mock<IReminderScheduler>().Object,
        NullLogger<UpdateAppointmentCommandHandler>.Instance);

    /// <summary>An elapsed visit the job has already started — the exact fixture from production.</summary>
    private Appointment RunningVisit()
    {
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, null, At, TimeSpan.FromMinutes(30));
        appointment.Start();

        _appointments.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment);
        return appointment;
    }

    // THE regression. Before the fix this returned IsSuccess with the status still InProgress.
    [Fact]
    public async Task Picking_Seance_Passee_By_Hand_Is_Refused_Rather_Than_Silently_Ignored()
    {
        var appointment = RunningVisit();

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Status = "AwaitingClosure" }, default);

        Assert.True(result.IsFailure);
        Assert.Contains("Séance passée", result.Error);
        // And the appointment is untouched — a refusal that had already mutated would be worse than the no-op.
        Assert.Equal(AppointmentStatus.InProgress, appointment.Status);
    }

    // The same, spelled the way `Enum.TryParse(..., ignoreCase: true)` also accepts it.
    [Fact]
    public async Task The_Refusal_Does_Not_Depend_On_The_Casing_Of_The_Posted_Value()
    {
        var appointment = RunningVisit();

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Status = "awaitingclosure" }, default);

        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// ⚠️ The other half of the refusal, and the reason it lives inside the `newStatus != oldStatus` gate: the
    /// edit dialog prepends the CURRENT status so its Select has a value, so every ordinary edit of a visit
    /// already in « Séance passée » posts that status back unchanged. Refusing there would make such a visit
    /// uneditable — a worse bug than the one being fixed, and reachable from any note or time change.
    /// </summary>
    [Fact]
    public async Task Re_Saving_A_Visit_Already_In_Seance_Passee_Is_Not_Refused()
    {
        var appointment = RunningVisit();
        appointment.MarkAwaitingClosure();

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand
            {
                Id = appointment.Id,
                Status = "AwaitingClosure",
                Notes = "Patient à rappeler",
            },
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.AwaitingClosure, appointment.Status);
        Assert.Equal("Patient à rappeler", appointment.Notes);
    }

    /// <summary>
    /// THE derived guard, and the one that would have caught this on the day the status was added: every
    /// manually-offered transition must actually MOVE the appointment. A status that the table admits but the
    /// handler's <c>switch</c> does not handle used to fall through to a successful save, so this asserts the
    /// outcome rather than the existence of an arm.
    /// </summary>
    [Fact]
    public async Task Every_Manually_Offered_Transition_Actually_Changes_The_Status()
    {
        var unmoved = new List<string>();

        foreach (var from in Enum.GetValues<AppointmentStatus>())
        {
            foreach (var to in Appointment.ManualNextStatusesFrom(from))
            {
                if (to == from)
                {
                    continue;
                }

                var appointment = ArrangeIn(from);
                if (appointment is null)
                {
                    continue; // Unreachable by any legal path — nothing to assert about it.
                }

                var result = await CreateHandler().Handle(
                    new UpdateAppointmentCommand
                    {
                        Id = appointment.Id,
                        Status = to.ToString(),
                        CancellationReason = to == AppointmentStatus.Cancelled ? "Motif" : null,
                    },
                    default);

                // Either it moved, or it was refused in as many words. Never « success, unchanged ».
                if (result.IsSuccess && appointment.Status != to)
                {
                    unmoved.Add($"{from} → {to} reported success and stayed {appointment.Status}");
                }
            }
        }

        Assert.Empty(unmoved);
    }

    /// <summary>
    /// The manual list is the domain table minus the job-written statuses — asserted in <b>both</b> directions
    /// over every status, so neither a widened table nor a widened job set can drift past it.
    /// </summary>
    [Fact]
    public void The_Manual_List_Is_The_Domain_Table_Minus_The_Job_Written_Statuses()
    {
        foreach (var from in Enum.GetValues<AppointmentStatus>())
        {
            var manual = Appointment.ManualNextStatusesFrom(from);

            Assert.DoesNotContain(AppointmentStatus.AwaitingClosure, manual);
            Assert.All(manual, s => Assert.Contains(s, Appointment.NextStatusesFrom(from)));
            Assert.Equal(
                Appointment.NextStatusesFrom(from).Where(s => !Appointment.IsJobWritten(s)).ToArray(),
                manual.ToArray());
        }
    }

    /// <summary>
    /// ⚠️ Withholding a status must not leave a visit with NO action, because
    /// <c>appointment-quick-actions</c> disables its whole « ⋯ » menu on
    /// <c>allowedNextStatuses.length === 0</c> — so a status whose only remaining option was the withheld one
    /// would present as a dead control on the agenda, with nothing to click and no explanation. Every status
    /// that had a manual move before must still have one.
    /// </summary>
    [Fact]
    public void Withholding_Seance_Passee_Never_Leaves_A_Visit_With_No_Action()
    {
        var stranded = Enum.GetValues<AppointmentStatus>()
            .Where(s => Appointment.NextStatusesFrom(s).Count > 0)
            .Where(s => Appointment.ManualNextStatusesFrom(s).Count == 0)
            .ToList();

        Assert.Empty(stranded);
    }

    /// <summary>
    /// ⚠️ The job's transition stays legal — this is what stops a future reading of « remove it from the table »
    /// from breaking the only writer. <c>AwaitingClosure</c> is withheld from humans, not from the product.
    /// </summary>
    [Fact]
    public void The_Progress_Job_Can_Still_Close_An_Elapsed_Visit()
    {
        Assert.True(Appointment.CanTransition(AppointmentStatus.Scheduled, AppointmentStatus.AwaitingClosure));
        Assert.True(Appointment.CanTransition(AppointmentStatus.InProgress, AppointmentStatus.AwaitingClosure));

        var appointment = RunningVisit();
        appointment.MarkAwaitingClosure();
        Assert.Equal(AppointmentStatus.AwaitingClosure, appointment.Status);
    }

    /// <summary>
    /// No appointment read may project <c>AllowedNextStatuses</c> from <c>NextStatusesFrom</c> — that field
    /// drives a control a person clicks. Four sites did, which is the repository's dominant defect shape: a
    /// correct rule wired to one call site. The frontend's own filter could not save them, because it applied
    /// only to the branch taken when the server sends nothing.
    /// </summary>
    [Fact]
    public void No_Appointment_Read_Projects_The_Human_List_From_The_Domain_Table()
    {
        var root = SolutionRoot();
        var handWritten = new Regex(
            @"AllowedNextStatuses\s*=\s*Appointment\.NextStatusesFrom", RegexOptions.Compiled);

        // ⚠️ The red proof: a derived guard fails OPEN, so the pattern must match the literal it forbids.
        Assert.Matches(
            handWritten,
            "AllowedNextStatuses = Appointment.NextStatusesFrom(appointment.Status).Select(s => s.ToString())");

        var scanned = 0;
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/obj/") || relative.Contains("/bin/"))
            {
                continue;
            }

            if (relative.EndsWith("Features/Appointments/AppointmentManualStatusTests.cs"))
            {
                continue;
            }

            scanned++;
            if (handWritten.IsMatch(File.ReadAllText(file)))
            {
                offenders.Add(relative);
            }
        }

        Assert.True(scanned > 100, $"Only {scanned} files scanned — this guard would pass while checking nothing.");
        Assert.Empty(offenders);
    }

    /// <summary>Arrange an appointment in <paramref name="status"/> through legal moves only, or null.</summary>
    private Appointment? ArrangeIn(AppointmentStatus status)
    {
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, null, At, TimeSpan.FromMinutes(30));

        switch (status)
        {
            case AppointmentStatus.Scheduled:
                break;
            case AppointmentStatus.InProgress:
                appointment.Start();
                break;
            case AppointmentStatus.AwaitingClosure:
                appointment.MarkAwaitingClosure();
                break;
            case AppointmentStatus.Completed:
                appointment.Complete();
                break;
            case AppointmentStatus.Cancelled:
                appointment.Cancel("Motif");
                break;
            case AppointmentStatus.NoShow:
                appointment.MarkAsNoShow();
                break;
            // Legacy-only: no transition leads to `Confirmed` any more, so it cannot be arranged.
            case AppointmentStatus.Confirmed:
            default:
                return null;
        }

        _appointments.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment);
        return appointment;
    }

    /// <summary>
    /// <c>[CallerFilePath]</c> rather than the assembly location — `FollowedTreatmentLifecycleTests`' reason:
    /// the test host's base directory can sit under a junction that throws before the scan starts. It
    /// <b>throws</b> rather than skipping, because a derived guard that silently scans nothing is worse than
    /// no guard at all.
    /// </summary>
    private static string SolutionRoot([CallerFilePath] string thisFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ClinicManagement.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                $"ClinicManagement.sln not found above '{thisFile}' — this guard would otherwise scan nothing "
                + "and pass, which is indistinguishable from finding no offenders.");
        }

        return directory.FullName;
    }
}
