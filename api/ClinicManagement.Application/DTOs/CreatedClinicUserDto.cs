namespace ClinicManagement.Application.DTOs;

/// <summary>
/// An admin-created staff account and the one-time password to hand over (<c>multi-tenant-cloud</c> US-3).
///
/// <para>Deliberately its own shape rather than <see cref="ClinicUserDto"/> plus a password field: the password is
/// returned <b>exactly once</b> and is never readable again, so it must not travel on the type the users list is
/// built from — where a future <c>GET</c> would then be one property away from serving it. Same reasoning as
/// <see cref="ResetPasswordResultDto"/>, which this mirrors.</para>
/// </summary>
public class CreatedClinicUserDto
{
    public string UserId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string Role { get; set; } = string.Empty;
    /// <summary>Null when the colleague was invited by e-mail (online) — she chooses her own password.</summary>
    public string? TemporaryPassword { get; set; }

    /// <summary>Online: whether the invitation e-mail left. False means « copiez le lien » — the account exists.</summary>
    public bool InvitationEmailed { get; set; }

    /// <summary>Online: the invitation link, returned once (only its hash is stored).</summary>
    public string? InvitationUrl { get; set; }
}
