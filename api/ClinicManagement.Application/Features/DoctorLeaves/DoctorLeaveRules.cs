using System.Globalization;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.DoctorLeaves;

/// <summary>
/// What a congé says in French, who may change one, and which booked visits it covers — shared by the three
/// write commands and the booking guard so the sentence a dentist reads is the same everywhere.
/// </summary>
public static class DoctorLeaveRules
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>The practitioner or the congé is not this cabinet's, or does not exist — answered 404.</summary>
    public const string NotFoundCode = "doctor_leave_not_found";

    public const string PractitionerNotFound = "Praticien introuvable.";
    public const string LeaveNotFound = "Congé introuvable.";
    public const string RetiredPractitioner = "Ce praticien a quitté le cabinet : aucun congé ne peut lui être ajouté.";
    public const string NotOwnLeave = "Vous ne pouvez modifier que vos propres congés.";

    /// <summary>« du 12/08 au 20/08 », « le 12/08 »; the year appears only when the two ends differ in it.</summary>
    public static string Period(DateOnly start, DateOnly end)
    {
        var format = start.Year == end.Year ? "dd/MM" : "dd/MM/yyyy";
        return start == end
            ? $"le {start.ToString(format, French)}"
            : $"du {start.ToString(format, French)} au {end.ToString(format, French)}";
    }

    public static string OnLeaveSentence(string practitioner, DoctorLeave leave) =>
        $"{practitioner} est en congé {Period(leave.StartDate, leave.EndDate)}.";

    public static string OverlapSentence(string practitioner, DoctorLeave existing) =>
        $"{practitioner} est déjà en congé {Period(existing.StartDate, existing.EndDate)}.";

    public static DoctorLeaveDto ToDto(this DoctorLeave leave, string doctorName) => new()
    {
        Id = leave.Id,
        DoctorId = leave.DoctorId,
        DoctorName = doctorName,
        StartDate = leave.StartDate,
        EndDate = leave.EndDate,
        DayCount = leave.DayCount,
        Note = leave.Note,
        Version = leave.Version,
    };

    /// <summary>
    /// The caller's clinic and the practitioner whose congés they may change: an admin or a secretary any of the
    /// cabinet's, a doctor their own only. A practitioner from another cabinet reads as not found.
    /// </summary>
    public static async Task<Result<Doctor>> ResolveEditableAsync(
        IClinicContext clinicContext,
        IUserRepository users,
        IDoctorRepository doctors,
        Guid doctorId,
        CancellationToken cancellationToken)
    {
        var userId = clinicContext.GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Result<Doctor>.Failure("Session invalide, veuillez vous reconnecter.");

        var user = await users.GetByAuth0SubAsync(userId, cancellationToken);
        if (user == null)
            return Result<Doctor>.Failure("Utilisateur introuvable.");

        var doctor = await doctors.GetByIdAsync(doctorId, cancellationToken);
        if (doctor == null || doctor.ClinicId != user.ClinicId)
            return Result<Doctor>.Failure(PractitionerNotFound, NotFoundCode);

        var isOwnRecord = doctor.UserId != null && doctor.UserId == user.Id;
        if (!user.IsAdmin() && !user.IsSecretary() && !isOwnRecord)
            return Result<Doctor>.Failure(NotOwnLeave);

        return Result<Doctor>.Success(doctor);
    }

    /// <summary>The congé, when it belongs to this practitioner of this cabinet; anything else reads as not found.</summary>
    public static async Task<Result<DoctorLeave>> ResolveLeaveAsync(
        IDoctorLeaveRepository leaves, Doctor doctor, Guid leaveId, CancellationToken cancellationToken)
    {
        var leave = await leaves.GetByIdAsync(leaveId, cancellationToken);
        return leave == null || leave.ClinicId != doctor.ClinicId || leave.DoctorId != doctor.Id
            ? Result<DoctorLeave>.Failure(LeaveNotFound, NotFoundCode)
            : Result<DoctorLeave>.Success(leave);
    }

    public static DoctorLeaveSaveResultDto SaveResult(DoctorLeave leave, string doctorName, (int Count, DateOnly? FirstDay) during) => new()
    {
        Leave = leave.ToDto(doctorName),
        AppointmentsDuring = during.Count,
        FirstAppointmentDay = during.FirstDay,
    };

    /// <summary>Booked patient visits of this practitioner still holding their slot inside the congé; none is touched.</summary>
    public static async Task<(int Count, DateOnly? FirstDay)> AppointmentsDuringAsync(
        IAppointmentRepository appointments,
        Guid clinicId,
        Guid doctorId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var from = ClinicClock.StartOfLocalDayUtc(startDate.ToDateTime(TimeOnly.MinValue));
        var to = ClinicClock.LastTickOfLocalDayUtc(endDate.ToDateTime(TimeOnly.MinValue));
        var booked = (await appointments.GetByClinicIdAsync(clinicId, from, to, doctorId, cancellationToken: cancellationToken))
            .Where(a => a.DoctorId == doctorId && a.PatientId != null && AppointmentScheduling.OccupiesSlot(a))
            .OrderBy(a => a.AppointmentDateTime)
            .ToList();

        DateOnly? firstDay = booked.Count == 0
            ? null
            : DateOnly.FromDateTime(ClinicClock.ToClinicLocal(AppointmentScheduling.NormalizeUtc(booked[0].AppointmentDateTime)));
        return (booked.Count, firstDay);
    }
}
