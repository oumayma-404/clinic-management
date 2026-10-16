namespace ClinicManagement.Domain.ValueObjects;

/// <summary>A quick message as it was when sent — a later rename or removal never rewrites a sent message (AC-6.4).</summary>
public sealed record StaffQuickMessageSnapshot(string Key, string Label, bool IsUrgent);
