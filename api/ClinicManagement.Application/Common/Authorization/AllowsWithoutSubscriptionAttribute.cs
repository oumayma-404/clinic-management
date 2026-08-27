namespace ClinicManagement.Application.Common.Authorization;

/// <summary>
/// This endpoint keeps working on a cabinet whose entitlement has ended (FR-3). Read from endpoint metadata by
/// the API's subscription gate; it grants nothing else, and it never widens authorization.
///
/// <para><b>The reason is mandatory, and that is the whole design.</b> The exempt set is a fixed list stated as
/// <i>what each endpoint is</i>, so every entry has to be able to answer « why may an unpaid cabinet still do
/// this? » at the place a reader finds it. A boolean flag would let the set grow by copy-paste.</para>
///
/// <para>⚠️ A <b>GET</b> needs no attribute — the gate never inspects one, which is what makes « an expired cabinet
/// can read and export everything » structural rather than a list somebody maintains (AC-4.1). Where a read-only
/// endpoint carries one anyway it is documentation: the exempt set is stated as what, so a reader does not have to
/// re-derive « is a GET refused? » to know. <c>SubscriptionExemptionCoverageTests</c> therefore classifies
/// <b>non-GET</b> actions only, and a green suite is not evidence that a GET-only row is load-bearing.</para>
///
/// <para>It lives beside <c>AuthorizationPolicies</c> rather than in the API project because it is the same kind of
/// thing — a per-endpoint declaration the controllers annotate themselves with — and the controllers already import
/// this namespace for their policies.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AllowsWithoutSubscriptionAttribute : Attribute
{
    public AllowsWithoutSubscriptionAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Une exemption d'abonnement doit indiquer sa raison.", nameof(reason));
        }

        Reason = reason;
    }

    /// <summary>Why this endpoint is not clinic work an unpaid cabinet should be refused.</summary>
    public string Reason { get; }
}
