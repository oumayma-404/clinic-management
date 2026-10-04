using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Clinics.Commands;

/// <summary>
/// « Afficher l'argent ». The controller spends a <see cref="ClinicMoneyMask.ShowStepUpAction"/> confirmation
/// before sending this, and the step-up mints that one from an authenticator code only. Once shown it stays shown.
/// </summary>
public class ShowMoneyCommand : IRequest<Result<ClinicMoneyMaskDto>>
{
}

public class ShowMoneyCommandHandler : IRequestHandler<ShowMoneyCommand, Result<ClinicMoneyMaskDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public ShowMoneyCommandHandler(
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

    public async Task<Result<ClinicMoneyMaskDto>> Handle(ShowMoneyCommand request, CancellationToken cancellationToken)
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
                return Result<ClinicMoneyMaskDto>.Failure("Seuls les administrateurs peuvent réafficher l'argent.");
            }

            var clinic = await _clinicRepository.GetByIdAsync(admin.ClinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<ClinicMoneyMaskDto>.Failure("Clinique introuvable.");
            }

            if (clinic.IsMoneyHidden)
            {
                clinic.ShowMoney();
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
