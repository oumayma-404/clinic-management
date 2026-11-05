namespace ClinicManagement.Domain.Enums;

/// <summary>What a secretary's congé is. Persisted as an int: append new members, never insert.</summary>
public enum StaffLeaveType
{
    /// <summary>« Congé payé » — the only type that uses up the yearly allowance.</summary>
    PaidLeave = 0,
    Sick = 1,
    Unpaid = 2,
    Other = 3,
}
