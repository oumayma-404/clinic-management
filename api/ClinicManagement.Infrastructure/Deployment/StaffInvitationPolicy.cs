using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Infrastructure.Deployment;

/// <summary>The kind half of « invite a colleague by e-mail » (<c>guided-first-days</c> C) — no setting can flip it.</summary>
public class StaffInvitationPolicy : IStaffInvitationPolicy
{
    private readonly DeploymentProfile _profile;

    public StaffInvitationPolicy(DeploymentProfile profile)
    {
        _profile = profile;
    }

    public bool InvitesStaffByLink => _profile.InvitesStaffByLink;
}
