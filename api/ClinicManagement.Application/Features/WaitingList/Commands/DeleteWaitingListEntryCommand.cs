using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.WaitingList.Commands;

public class DeleteWaitingListEntryCommand : IRequest<Result<bool>>
{
    public Guid Id { get; set; }
}

public class DeleteWaitingListEntryCommandHandler : IRequestHandler<DeleteWaitingListEntryCommand, Result<bool>>
{
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteWaitingListEntryCommandHandler(
        IWaitingListRepository waitingListRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _waitingListRepository = waitingListRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(DeleteWaitingListEntryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<bool>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var entry = await _waitingListRepository.GetByIdAsync(request.Id, cancellationToken);
            if (entry == null || entry.ClinicId != clinic.Value)
                return Result<bool>.Failure("Entrée de liste d'attente introuvable.");

            await _waitingListRepository.DeleteAsync(request.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Erreur lors de la suppression de l'entrée de liste d'attente : {ex.Message}");
        }
    }
}
