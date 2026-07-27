using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Queries;

public class GetClinicLogoQuery : IRequest<Result<ClinicLogoDto>>
{
}

public class ClinicLogoDto
{
    public Stream FileStream { get; set; } = null!;
    public string ContentType { get; set; } = "image/png";
}

public class GetClinicLogoQueryHandler : IRequestHandler<GetClinicLogoQuery, Result<ClinicLogoDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IFileStorage _fileStorage;

    public GetClinicLogoQueryHandler(
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IFileStorage fileStorage)
    {
        _clinicRepository = clinicRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _fileStorage = fileStorage;
    }

    public async Task<Result<ClinicLogoDto>> Handle(GetClinicLogoQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Get user ID from token
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<ClinicLogoDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<ClinicLogoDto>.Failure("Utilisateur introuvable.");
            }

            // Get clinic from database
            var clinic = await _clinicRepository.GetByIdAsync(user.ClinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<ClinicLogoDto>.Failure("Clinique introuvable.");
            }

            if (string.IsNullOrWhiteSpace(clinic.LogoUrl))
            {
                return Result<ClinicLogoDto>.Failure("Logo de la clinique introuvable.");
            }

            // Download logo from MinIO
            var fileStream = await _fileStorage.DownloadAsync(clinic.LogoUrl, cancellationToken);

            var dto = new ClinicLogoDto
            {
                FileStream = fileStream,
                ContentType = "image/png" // Default, could be improved by storing content type
            };

            return Result<ClinicLogoDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<ClinicLogoDto>.Failure($"Error getting clinic logo: {ex.Message}");
        }
    }
}



