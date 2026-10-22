namespace ClinicManagement.Application.Common.Authorization;

/// <summary>
/// FR-5's « online only » list (<c>clinic-pc-copy</c>): while a PC de secours holds the cabinet's saves during a cut,
/// this action is refused there with AC-3.5's sentence — creating or deactivating an account, a password, an
/// authenticator or a role, « Rappels » settings, Google Agenda, the clinic archive. Read from endpoint metadata by the
/// API's <c>RelayLeaseGateMiddleware</c>, whatever the HTTP method; it changes nothing anywhere else.
///
/// <para>⚠️ It is the other face of <see cref="AllowedWhileCloudFencedAttribute"/>: what the cloud keeps writing during a
/// cut, the PC must not write, or the return would bring back two versions of one account. <c>RelayOnlineOnlyCoverageTests</c>
/// holds the pairing. The reason is mandatory, on <see cref="AllowsWithoutSubscriptionAttribute"/>'s design.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class OnlineOnlyAttribute : Attribute
{
    public OnlineOnlyAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Une action « en ligne uniquement » doit indiquer sa raison.", nameof(reason));
        }

        Reason = reason;
    }

    /// <summary>Why this action waits for the internet on a PC de secours in charge.</summary>
    public string Reason { get; }
}
