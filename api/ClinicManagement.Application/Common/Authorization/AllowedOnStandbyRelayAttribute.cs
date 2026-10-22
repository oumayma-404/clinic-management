namespace ClinicManagement.Application.Common.Authorization;

/// <summary>
/// This write keeps working on a PC de secours that is only holding a copy (<c>clinic-pc-copy</c>, standby). Read from
/// endpoint metadata by the API's <c>RelayLeaseGateMiddleware</c>; it grants nothing else.
///
/// <para>The set is the sign-in doors alone: anything else written on a copy is overwritten by the next change the
/// cloud sends, or — worse — is never seen by the cloud at all. The reason is mandatory, on
/// <see cref="AllowsWithoutSubscriptionAttribute"/>'s design, and <c>RelayStandbyExemptionCoverageTests</c> holds the
/// set in both directions.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AllowedOnStandbyRelayAttribute : Attribute
{
    public AllowedOnStandbyRelayAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Une exemption du PC de secours doit indiquer sa raison.", nameof(reason));
        }

        Reason = reason;
    }

    /// <summary>Why this write is safe on a copy the cloud overwrites.</summary>
    public string Reason { get; }
}
