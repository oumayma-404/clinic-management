using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.StaffChat;

/// <summary>
/// A message's patient and appointment (FR-3): checked when sent, and shown as they are NOW when read — an archived
/// patient, a cancelled appointment, a removed record — never as they were. A link never blocks deleting either.
/// </summary>
public static class StaffMessageLinks
{
    /// <summary>The links as sent, or a refusal; an appointment alone brings its patient with it.</summary>
    public static async Task<Result<(Guid? PatientId, Guid? AppointmentId)>> ValidateAsync(
        Guid? patientId, Guid? appointmentId, StaffChatRoster roster, IPatientRepository patients,
        IStaffMessageRepository messages, CancellationToken cancellationToken)
    {
        if (appointmentId is { } aid)
        {
            var appointment = (await messages.GetLinkedAppointmentsAsync(roster.ClinicId, new[] { aid }, cancellationToken))
                .FirstOrDefault();
            if (appointment is null || appointment.Disregarded || appointment.PatientId is null
                || (patientId is not null && appointment.PatientId != patientId))
            {
                return Result<(Guid?, Guid?)>.Failure(
                    StaffChatRefusals.AppointmentNotFound, StaffChatRefusals.AppointmentNotFoundCode);
            }

            patientId = appointment.PatientId;
        }

        if (patientId is { } pid)
        {
            var patient = await patients.GetByIdAsync(pid, cancellationToken);
            if (patient is null || patient.ClinicId != roster.ClinicId)
            {
                return Result<(Guid?, Guid?)>.Failure(StaffChatRefusals.PatientNotFound, StaffChatRefusals.PatientNotFoundCode);
            }
        }

        return Result<(Guid?, Guid?)>.Success((patientId, appointmentId));
    }

    /// <summary>Fills each DTO's links from its message, in two batched reads for the whole set.</summary>
    public static async Task FillAsync(
        IReadOnlyCollection<(StaffMessage Message, StaffMessageDto Dto)> pairs, StaffChatRoster roster,
        IPatientRepository patients, IStaffMessageRepository messages, CancellationToken cancellationToken)
    {
        var patientIds = pairs.Select(p => p.Message.PatientId).OfType<Guid>().Distinct().ToList();
        var appointmentIds = pairs.Select(p => p.Message.AppointmentId).OfType<Guid>().Distinct().ToList();
        if (patientIds.Count == 0 && appointmentIds.Count == 0)
        {
            return;
        }

        var patientById = patientIds.Count == 0
            ? new Dictionary<Guid, Patient>() as IReadOnlyDictionary<Guid, Patient>
            : await patients.GetByIdsAsync(roster.ClinicId, patientIds, cancellationToken);
        var appointmentById = (await messages.GetLinkedAppointmentsAsync(roster.ClinicId, appointmentIds, cancellationToken))
            .ToDictionary(a => a.Id);

        foreach (var (message, dto) in pairs)
        {
            if (message.PatientId is { } pid)
            {
                dto.Patient = patientById.TryGetValue(pid, out var patient)
                    ? new StaffMessagePatientDto { Id = pid, Name = NameOf(patient), Archived = patient.IsArchived }
                    : new StaffMessagePatientDto { Id = pid, Missing = true };
            }

            if (message.AppointmentId is { } aid)
            {
                dto.Appointment = appointmentById.TryGetValue(aid, out var appointment) && !appointment.Disregarded
                    ? new StaffMessageAppointmentDto
                    {
                        Id = aid,
                        StartsAt = appointment.StartsAt,
                        DentistName = roster.Dentists.FirstOrDefault(d => d.DoctorId == appointment.DoctorId)?.Name,
                        Cancelled = appointment.Status == AppointmentStatus.Cancelled,
                    }
                    : new StaffMessageAppointmentDto { Id = aid, Missing = true };
            }
        }
    }

    /// <summary>One message's DTO with its links.</summary>
    public static async Task<StaffMessageDto> ToDtoAsync(
        StaffMessage message, StaffChatRoster roster, IPatientRepository patients, IStaffMessageRepository messages,
        CancellationToken cancellationToken)
    {
        var dto = StaffChatInbox.ToMessageDto(message, roster);
        await FillAsync(new[] { (message, dto) }, roster, patients, messages, cancellationToken);
        return dto;
    }

    private static string NameOf(Patient patient) => $"{patient.FirstName} {patient.LastName}".Trim();
}
