using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Common.Services;

public class CurrentClinicResolver : ICurrentClinicResolver
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;

    public CurrentClinicResolver(IClinicContext clinicContext, IUserRepository userRepository)
    {
        _clinicContext = clinicContext;
        _userRepository = userRepository;
    }

    public async Task<Result<Guid>> GetClinicIdAsync(CancellationToken cancellationToken = default)
    {
        var userId = _clinicContext.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result<Guid>.Failure("Session invalide, veuillez vous reconnecter.");
        }

        var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
        if (user == null)
        {
            return Result<Guid>.Failure("Utilisateur introuvable.");
        }

        return Result<Guid>.Success(user.ClinicId);
    }
}
