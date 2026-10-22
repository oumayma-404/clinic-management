namespace ClinicManagement.Domain.Enums;

/// <summary>The three kinds of staff chat thread (staff-chat FR-1). Never persisted — it is read off the key.</summary>
public enum StaffConversationKind
{
    Direct = 0,
    Desk = 1,
    Clinic = 2,
}
