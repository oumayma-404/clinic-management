using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Appointments;

/// <summary>
/// The one place a visit's board state becomes a DTO, shared by the four sites that build an
/// <see cref="AppointmentDto"/> for <see cref="AppointmentProcedureMapping"/>'s reason: they must agree.
/// </summary>
public static class AppointmentWaitingRoomMapping
{
    public static AppointmentWaitingRoomDto ToWaitingRoomDto(this Appointment appointment) => new()
    {
        Lane = appointment.WaitingRoomLane.ToString(),
        ArrivedAtUtc = appointment.ArrivedAtUtc,
        CalledInAtUtc = appointment.CalledInAtUtc,
        LeftChairAtUtc = appointment.LeftChairAtUtc,
        IsWalkIn = appointment.IsWalkIn,
    };
}
