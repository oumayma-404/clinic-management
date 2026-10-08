namespace ClinicManagement.Application.Common.Authorization;

/// <summary>
/// This write keeps working on the cloud while a cabinet's PC de secours may hold its saves (<c>clinic-pc-copy</c>
/// FR-11, D15): signing in, an administrator's account changes, managing the PC de secours itself, and the PC's own
/// channel. Read from endpoint metadata by the API's <c>RelayLeaseGateMiddleware</c>; it grants nothing else.
///
/// <para>⚠️ Everything outside this set waits: a row saved on the cloud while the PC may be recording the cabinet's
/// work is a row the two copies would then disagree about. The reason is mandatory, on
/// <see cref="AllowsWithoutSubscriptionAttribute"/>'s design, and <c>RelayFenceExemptionCoverageTests</c> holds the set
/// in both directions.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AllowedWhileCloudFencedAttribute : Attribute
{
    public AllowedWhileCloudFencedAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Une exemption pendant la relève doit indiquer sa raison.", nameof(reason));
        }

        Reason = reason;
    }

    /// <summary>Why this write is safe while the PC de secours may be recording the cabinet's work.</summary>
    public string Reason { get; }
}
