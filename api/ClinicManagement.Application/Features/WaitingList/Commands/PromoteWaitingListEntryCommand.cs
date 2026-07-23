using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.WaitingList.Commands;

public class PromoteWaitingListEntryCommand : IRequest<Result<WaitingListEntryDto>>
{
    public Guid Id { get; set; }
    public Guid? ResultingAppointmentId { get; set; }
}

public class PromoteWaitingListEntryCommandHandler : IRequestHandler<PromoteWaitingListEntryCommand, Result<WaitingListEntryDto>>
{
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public PromoteWaitingListEntryCommandHandler(
        IWaitingListRepository waitingListRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _waitingListRepository = waitingListRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WaitingListEntryDto>> Handle(PromoteWaitingListEntryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<WaitingListEntryDto>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var entry = await _waitingListRepository.GetByIdAsync(request.Id, cancellationToken);
            if (entry == null || entry.ClinicId != clinic.Value)
                return Result<WaitingListEntryDto>.Failure("Entrée de liste d'attente introuvable.");

            entry.Promote(request.ResultingAppointmentId);

            await _waitingListRepository.UpdateAsync(entry, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<WaitingListEntryDto>.Success(entry.ToDto());
        }
        catch (InvalidOperationException ex)
        {
            return Result<WaitingListEntryDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            return Result<WaitingListEntryDto>.Failure($"Erreur lors de la conversion de l'entrée de liste d'attente : {ex.Message}");
        }
    }
}
