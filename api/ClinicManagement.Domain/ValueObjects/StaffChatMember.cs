namespace ClinicManagement.Domain.ValueObjects;

/// <summary>One account of the cabinet as the chat rules see it: who it is, whether it sits at the desk, whether it is still active.</summary>
public sealed record StaffChatMember(string UserId, bool IsSecretary, bool IsActive);
