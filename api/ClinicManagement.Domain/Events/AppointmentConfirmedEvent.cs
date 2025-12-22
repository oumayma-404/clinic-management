using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Events;

public class AppointmentConfirmedEvent : IDomainEvent
{
    public Guid AppointmentId { get; }
    public Guid PatientId { get; }
    public DateTime AppointmentDateTime { get; }
    public DateTime OccurredOn { get; }

    public AppointmentConfirmedEvent(Guid appointmentId, Guid patientId, DateTime appointmentDateTime)
    {
        AppointmentId = appointmentId;
        PatientId = patientId;
        AppointmentDateTime = appointmentDateTime;
        OccurredOn = DateTime.UtcNow;
    }
}



