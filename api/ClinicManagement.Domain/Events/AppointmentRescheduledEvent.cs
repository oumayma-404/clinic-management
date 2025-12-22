using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Events;

public class AppointmentRescheduledEvent : IDomainEvent
{
    public Guid AppointmentId { get; }
    public Guid PatientId { get; }
    public DateTime OldDateTime { get; }
    public DateTime NewDateTime { get; }
    public DateTime OccurredOn { get; }

    public AppointmentRescheduledEvent(Guid appointmentId, Guid patientId, DateTime oldDateTime, DateTime newDateTime)
    {
        AppointmentId = appointmentId;
        PatientId = patientId;
        OldDateTime = oldDateTime;
        NewDateTime = newDateTime;
        OccurredOn = DateTime.UtcNow;
    }
}



