namespace ClinicManagement.Domain.Enums;

/// <summary>Where one address of a news broadcast stands. Persisted as int — append members, never insert.</summary>
public enum PlatformNewsDeliveryStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,

    /// <summary>Claimed by one run, e-mail on its way. A claim no run confirmed is never re-sent.</summary>
    Sending = 3
}
