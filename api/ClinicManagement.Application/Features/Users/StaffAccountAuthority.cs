using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Users;

/// <summary>
/// Who may act on whose account. A gestionnaire manages every account except an administrator's, and may never
/// hand out the admin role — otherwise « less than admin » is one click from admin (owner's call, 2026-11-27).
/// <para>Every Users command that acts on a target or assigns a role asks here, after its tenant check.</para>
/// </summary>
public static class StaffAccountAuthority
{
    public const string AdminAccountRefusal = "Seul un administrateur peut modifier le compte d'un administrateur.";
    public const string AdminRoleRefusal = "Seul un administrateur peut nommer un administrateur.";

    /// <summary>The caller may change, reset or switch off <paramref name="target"/>'s account.</summary>
    public static bool MayManage(User caller, User target) =>
        caller.IsAdmin() || (caller.IsManager() && !target.IsAdmin());

    /// <summary>The caller may give someone <paramref name="role"/> (already normalised).</summary>
    public static bool MayGrant(User caller, string role) =>
        caller.IsAdmin() || (caller.IsManager() && role != User.RoleAdmin);
}
