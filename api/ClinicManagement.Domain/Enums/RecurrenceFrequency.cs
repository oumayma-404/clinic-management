namespace ClinicManagement.Domain.Enums;

/// <summary>How a recurring appointment series repeats. Stored as its name on <c>RecurringAppointment.RecurrencePattern</c>.</summary>
public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2
}
