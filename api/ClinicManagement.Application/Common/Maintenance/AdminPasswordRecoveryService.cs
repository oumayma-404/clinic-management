using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Common.Maintenance;

/// <summary>The outcome of a successful admin password reset: which admin was reset and the one-time temporary password to relay.</summary>
public record AdminPasswordRecoveryResult(string AdminId, string AdminEmail, string TemporaryPassword);

/// <summary>
/// Offline lockout recovery (FR-B6, Local mode only). Resets a local administrator's password to a
/// fresh temporary value and forces a change at next login. Invoked by the server-side console utility
/// (see <c>ClinicManagement.API</c> <c>reset-admin-password</c>) — deliberately NOT registered in DI so
/// it can never be injected into an HTTP handler and reset an admin without authentication.
/// </summary>
public class AdminPasswordRecoveryService
{
    private readonly IUserRepository _users;
    private readonly ILocalAuthService _localAuth;
    private readonly IUnitOfWork _unitOfWork;

    public AdminPasswordRecoveryService(
        IUserRepository users,
        ILocalAuthService localAuth,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _localAuth = localAuth;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Resets the target administrator's password. When <paramref name="email"/> is supplied the admin is
    /// looked up by it; otherwise the sole local administrator is used (an error is returned when zero or
    /// more than one exist, so the operator must disambiguate). On success the admin's lockout/failed-attempt
    /// state is cleared and <c>MustChangePassword</c> is set, and the temporary password is returned once.
    /// </summary>
    public async Task<Result<AdminPasswordRecoveryResult>> ResetAdminPasswordAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        User admin;

        if (!string.IsNullOrWhiteSpace(email))
        {
            var user = await _users.GetByEmailAsync(email.Trim(), cancellationToken);
            if (user is null || !user.IsLocalAccount())
            {
                return Result<AdminPasswordRecoveryResult>.Failure(
                    $"No local account was found for email '{email.Trim()}'.");
            }
            if (!user.IsAdmin())
            {
                return Result<AdminPasswordRecoveryResult>.Failure(
                    $"Account '{user.Email}' is not an administrator. This utility only recovers administrator accounts.");
            }
            admin = user;
        }
        else
        {
            var all = await _users.GetAllAsync(cancellationToken);
            var admins = all.Where(u => u.IsLocalAccount() && u.IsAdmin()).ToList();
            if (admins.Count == 0)
            {
                return Result<AdminPasswordRecoveryResult>.Failure(
                    "No local administrator account exists to recover.");
            }
            if (admins.Count > 1)
            {
                return Result<AdminPasswordRecoveryResult>.Failure(
                    $"Multiple administrator accounts exist ({admins.Count}). Re-run with the target admin's email, e.g. reset-admin-password admin@clinic.com.");
            }
            admin = admins[0];
        }

        var temporaryPassword = _localAuth.GenerateTemporaryPassword();
        var hash = _localAuth.HashPassword(temporaryPassword);
        // SetPassword also zeroes failed-attempt count and clears any active lockout — exactly what a
        // locked-out admin needs to get back in.
        admin.SetPassword(hash, mustChangePassword: true);

        _users.Update(admin);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<AdminPasswordRecoveryResult>.Success(
            new AdminPasswordRecoveryResult(admin.Id, admin.Email!, temporaryPassword));
    }
}
