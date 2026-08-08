using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Commands;

/// <summary>
/// Admin-only: regenerates the clinic's self-registration code (AC-4.5), invalidating the
/// old code for future staff registrations. Returns the clinic with its new code.
/// </summary>
public class RegenerateClinicCodeCommand : IRequest<Result<ClinicDto>>
{
}

public class RegenerateClinicCodeCommandHandler : IRequestHandler<RegenerateClinicCodeCommand, Result<ClinicDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public RegenerateClinicCodeCommandHandler(
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork)
    {
        _clinicRepository = clinicRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ClinicDto>> Handle(RegenerateClinicCodeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<ClinicDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var admin = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin == null)
            {
                return Result<ClinicDto>.Failure("Utilisateur introuvable.");
            }

            // AC-5.4 / AC-4.5: only an admin can regenerate the clinic code.
            if (!admin.IsAdmin())
            {
                return Result<ClinicDto>.Failure("Seuls les administrateurs peuvent régénérer le code de la clinique.");
            }

            var clinic = await _clinicRepository.GetByIdAsync(admin.ClinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<ClinicDto>.Failure("Clinique introuvable.");
            }

            var code = ClinicCodeGenerator.Generate();
            while (await _clinicRepository.CodeExistsAsync(code, cancellationToken))
            {
                code = ClinicCodeGenerator.Generate();
            }

            clinic.SetCode(code);
            await _clinicRepository.UpdateAsync(clinic, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<ClinicDto>.Success(new ClinicDto
            {
                Id = clinic.Id,
                Name = clinic.Name,
                Address = clinic.Address,
                Phone = clinic.Phone,
                Email = clinic.Email,
                Code = clinic.Code,
                LogoUrl = clinic.LogoUrl,
                CreatedAt = clinic.CreatedAt,
                Version = clinic.Version,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<ClinicDto>.Failure($"Error regenerating clinic code: {ex.Message}");
        }
    }
}
