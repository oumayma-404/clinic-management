namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Where a cabinet stands right now — <b>derived, computed, never stored</b> (FR-1). It is a function of the
/// entitlement's end date, the suspension flag and the clinic's own today, so storing it would be a fourth thing
/// that can disagree with the other three, and it would change at midnight with no write to change it.
/// </summary>
public enum SubscriptionState
{
    /// <summary>Inside the free days, and every capability is available — a trial is not a reduced product (AC-1.4).</summary>
    Trial,

    /// <summary>Paid up, or entitled open-ended.</summary>
    Active,

    /// <summary>Past the end date: reads and exports still work, writes are refused (US-4).</summary>
    Expired,

    /// <summary>Stopped by the vendor for a stated reason (FR-7). Outranks <see cref="Expired"/> — EC-11.</summary>
    Suspended
}
