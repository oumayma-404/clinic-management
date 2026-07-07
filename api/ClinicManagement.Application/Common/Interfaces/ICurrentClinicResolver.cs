using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Resolves the current authenticated user's clinic id from the JWT, falling back to the
/// database lookup (Auth0 sub -> User -> ClinicId). Returns a failure Result when the user
/// is not authenticated or not found.
/// </summary>
public interface ICurrentClinicResolver
{
    Task<Result<Guid>> GetClinicIdAsync(CancellationToken cancellationToken = default);
}
