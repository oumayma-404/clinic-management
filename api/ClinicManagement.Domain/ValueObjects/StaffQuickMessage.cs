using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.ValueObjects;

/// <summary>
/// One button of the cabinet's quick-message list. Owned by <see cref="Entities.StaffQuickMessageList"/>; the key is
/// what a sent message and the per-person ordering refer to, so a rename keeps both (AC-6.4, AC-2.2).
/// </summary>
public sealed class StaffQuickMessage
{
    public const int KeyMaxLength = 64;
    public const int LabelMaxLength = 40;

    public string Key { get; private set; }
    public string Label { get; private set; }
    public StaffQuickMessageAudience Audience { get; private set; }
    public bool AsksForPatient { get; private set; }
    public bool IsUrgent { get; private set; }
    public bool IsBuiltIn { get; private set; }
    public int SortOrder { get; private set; }

    private StaffQuickMessage()
    {
        // For EF Core.
        Key = string.Empty;
        Label = string.Empty;
    }

    public StaffQuickMessage(
        string key, string label, StaffQuickMessageAudience audience, bool asksForPatient, bool isUrgent, bool isBuiltIn,
        int sortOrder)
    {
        Key = key;
        Label = label;
        Audience = audience;
        AsksForPatient = asksForPatient;
        IsUrgent = isUrgent;
        IsBuiltIn = isBuiltIn;
        SortOrder = sortOrder;
    }

    public StaffQuickMessageSnapshot Snapshot() => new(Key, Label, IsUrgent);
}
