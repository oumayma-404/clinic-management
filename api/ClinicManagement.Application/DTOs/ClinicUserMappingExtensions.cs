using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

/// <summary>
/// The one <see cref="User"/> → <see cref="ClinicUserDto"/> mapping.
///
/// <para>⚠️ Three copies existed — the list, the role change and the activation toggle — and they had <b>already
/// drifted</b>: only the list set <c>IsPendingActivation</c>, so the two write paths answered « false » for an
/// account that has never signed in, and the row that came back from a save contradicted the row beside it. That
/// is the argument for one mapper, made by the code before anyone added a field to it.</para>
/// </summary>
public static class ClinicUserMappingExtensions
{
    public static ClinicUserDto ToClinicUserDto(this User user) => new()
    {
        Id = user.Id,
        ClinicId = user.ClinicId,
        Role = user.Role,
        Email = user.Email,
        FullName = user.FullName,
        IsActive = user.IsActive,
        IsPendingActivation = user.IsPendingActivation,
        MustChangePassword = user.MustChangePassword,
        LastLoginAt = user.LastLoginAt,
        Version = user.Version,
        CreatedAt = user.CreatedAt,
    };
}
