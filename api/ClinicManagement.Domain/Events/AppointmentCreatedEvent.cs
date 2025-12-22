using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Events;

public class AppointmentCreatedEvent : IDomainEvent
{
    public Guid AppointmentId { get; }
    public Guid PatientId { get; }
    public DateTime AppointmentDateTime { get; }
    public DateTime OccurredOn { get; }

    public AppointmentCreatedEvent(Guid appointmentId, Guid patientId, DateTime appointmentDateTime)
    {
        AppointmentId = appointmentId;
        PatientId = patientId;
        AppointmentDateTime = appointmentDateTime;
        OccurredOn = DateTime.UtcNow;
    }
}



