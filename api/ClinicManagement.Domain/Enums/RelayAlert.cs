namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Which PC de secours problem one admin bell row names (<c>clinic-pc-copy</c> AC-2.2, EC-9, EC-10). The dedupe key
/// of <see cref="NotificationCategory.RelayAttention"/>: one row per member at most, per cabinet.
///
/// <para>⚠️ Persisted as an int — append, never insert.</para>
/// </summary>
public enum RelayAlert
{
    Off = 1,
    Late = 2,
    DiskNearlyFull = 3,
    Mismatch = 4,
    Abandoned = 5,
    ClockWrong = 6,
    Stopped = 7,
    // Appended, never inserted: persisted as int on StaffNotification.RelayAlert.
    Silent = 8,

    /// <summary>D18 / AC-5.9: the PC has failed to hand the cut back for 15 min; the cabinet keeps working on it.</summary>
    ReturnStuck = 9,

    /// <summary>D18 / AC-5.6: lines of « Modifications à vérifier » nobody has marked « Vu » yet.</summary>
    ToReview = 10,
}
