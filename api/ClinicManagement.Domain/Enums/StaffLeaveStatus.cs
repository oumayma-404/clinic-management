namespace ClinicManagement.Domain.Enums;

/// <summary>Where a congé request stands. Persisted as an int: append new members, never insert.</summary>
public enum StaffLeaveStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
}
