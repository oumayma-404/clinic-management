namespace ClinicManagement.Domain.Enums;

/// <summary>Who a quick message is for (staff-chat FR-4). Stored as int — append only, never reorder.</summary>
public enum StaffQuickMessageAudience
{
    Dentist = 0,
    Desk = 1,
    Reply = 2,
}
