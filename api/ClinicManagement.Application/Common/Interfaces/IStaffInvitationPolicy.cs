namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Whether a colleague's account is created with an e-mailed link to choose her own password (<c>guided-first-days</c>
/// C) rather than with a temporary password the administrator passes on. The seam exists because
/// <c>DeploymentProfile</c> lives in Infrastructure and Application references Domain alone —
/// <see cref="ISecondFactorPolicy"/>'s reason.
/// </summary>
public interface IStaffInvitationPolicy
{
    bool InvitesStaffByLink { get; }
}
