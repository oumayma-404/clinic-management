using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Clinics.Commands;

/// <summary>
/// « Masquer l'argent » — one click, no code. Refused to an admin with no authenticator, because showing it again
/// needs one and a cabinet must never hide what it cannot bring back.
/// </summary>
public class HideMoneyCommand : IRequest<Result<ClinicMoneyMaskDto>>
{
}

public class HideMoneyCommandHandler : IRequestHandler<HideMoneyCommand, Result<ClinicMoneyMaskDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public HideMoneyCommandHandler(
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

    public async Task<Result<ClinicMoneyMaskDto>> Handle(HideMoneyCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<ClinicMoneyMaskDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var admin = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin == null)
            {
                return Result<ClinicMoneyMaskDto>.Failure("Utilisateur introuvable.");
            }

            if (!admin.IsAdmin())
            {
                return Result<ClinicMoneyMaskDto>.Failure("Seuls les administrateurs peuvent masquer l'argent.");
            }

            if (!admin.IsTotpEnrolled)
            {
                return Result<ClinicMoneyMaskDto>.Failure(ClinicMoneyMask.NotEnrolled, ClinicAuthRefusals.TotpNotEnrolled);
            }

            var clinic = await _clinicRepository.GetByIdAsync(admin.ClinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<ClinicMoneyMaskDto>.Failure("Clinique introuvable.");
            }

            // Already hidden: nothing to write, so no audit row and no realtime noise for a double click.
            if (!clinic.IsMoneyHidden)
            {
                clinic.HideMoney();
                await _clinicRepository.UpdateAsync(clinic, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<ClinicMoneyMaskDto>.Success(new ClinicMoneyMaskDto(clinic.IsMoneyHidden));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<ClinicMoneyMaskDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
