using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Commands;

/// <summary>
/// Returns the cabinet's documents to the text header. Deletes no blob: documents issued with the letterhead still
/// point at it.
/// </summary>
public class RemoveClinicLetterheadCommand : IRequest<Result<ClinicLetterheadDto>>
{
    public uint Version { get; set; }
}

public class RemoveClinicLetterheadCommandHandler
    : IRequestHandler<RemoveClinicLetterheadCommand, Result<ClinicLetterheadDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RemoveClinicLetterheadCommandHandler> _logger;

    public RemoveClinicLetterheadCommandHandler(
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        ILogger<RemoveClinicLetterheadCommandHandler> logger)
    {
        _clinicRepository = clinicRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ClinicLetterheadDto>> Handle(
        RemoveClinicLetterheadCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            var user = string.IsNullOrEmpty(userId)
                ? null
                : await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<ClinicLetterheadDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            if (!user.IsAdmin())
            {
                return Result<ClinicLetterheadDto>.Failure(LetterheadRules.AdminOnly);
            }

            var clinic = await _clinicRepository.GetByIdAsync(user.ClinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<ClinicLetterheadDto>.Failure("Clinique introuvable.");
            }

            clinic.RemoveLetterhead();
            _unitOfWork.SetExpectedVersion(clinic, request.Version);
            await _clinicRepository.UpdateAsync(clinic, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<ClinicLetterheadDto>.Success(ClinicLetterheadDto.From(clinic));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error removing the clinic letterhead");
            return Result<ClinicLetterheadDto>.Failure("Erreur lors de la suppression de l'en-tête.");
        }
    }
}
