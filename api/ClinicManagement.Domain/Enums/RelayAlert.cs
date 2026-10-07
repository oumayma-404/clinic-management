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
}
