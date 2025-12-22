using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Events;

public class PatientFlagAddedEvent : IDomainEvent
{
    public Guid PatientId { get; }
    public Guid FlagId { get; }
    public DateTime OccurredOn { get; }

    public PatientFlagAddedEvent(Guid patientId, Guid flagId)
    {
        PatientId = patientId;
        FlagId = flagId;
        OccurredOn = DateTime.UtcNow;
    }
}



