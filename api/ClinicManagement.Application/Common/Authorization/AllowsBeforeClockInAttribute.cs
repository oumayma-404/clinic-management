namespace ClinicManagement.Application.Common.Authorization;

/// <summary>
/// This write keeps working while a secretary's account waits for her arrival at the borne (<c>pointage-obligatoire</c>
/// AC-3). Read from endpoint metadata by the API's <c>AttendanceLockMiddleware</c>; it grants nothing else and never
/// widens authorization.
///
/// <para><b>The reason is mandatory</b>, for <see cref="AllowsWithoutSubscriptionAttribute"/>'s reason: the open set is a
/// reviewed list stated as <i>what each endpoint is</i>, so every entry answers « why may she do this before clocking
/// in? » where a reader finds it. A GET needs none — the gate never inspects a read.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AllowsBeforeClockInAttribute : Attribute
{
    public AllowsBeforeClockInAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Une exemption du pointage doit indiquer sa raison.", nameof(reason));

        Reason = reason;
    }

    /// <summary>Why this endpoint is not work that should wait for her arrival.</summary>
    public string Reason { get; }
}
