namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Why a cabinet is covered for a stretch of time (<c>clinic-subscription</c> FR-2). Every entry of the ledger
/// carries one, and the kind is a description of its origin — it grants nothing the duration does not.
/// </summary>
public enum SubscriptionPeriodKind
{
    /// <summary>The 30 free days a cabinet gets on arrival, with no card (AC-1.1).</summary>
    Trial = 1,

    /// <summary>A payment the vendor received and recorded (AC-5.1).</summary>
    Paid = 2,

    /// <summary>
    /// A cabinet that existed before subscriptions did, entitled open-ended (AC-6.1, AC-6.2). Its
    /// <c>Note</c> records that it was grandfathered and why, which is the whole of AC-6.2.
    /// </summary>
    Grandfathered = 3,

    /// <summary>Time given rather than sold — a goodwill extension, a pilot, an apology.</summary>
    Complimentary = 4
}
