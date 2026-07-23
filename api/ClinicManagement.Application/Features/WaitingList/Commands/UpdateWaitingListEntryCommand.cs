using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.WaitingList.Commands;

public class UpdateWaitingListEntryCommand : IRequest<Result<WaitingListEntryDto>>
{
    public Guid Id { get; set; }
    public Guid? PreferredDoctorId { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string? DesiredTimeframe { get; set; }
    public string? Note { get; set; }
}

public class UpdateWaitingListEntryCommandHandler : IRequestHandler<UpdateWaitingListEntryCommand, Result<WaitingListEntryDto>>
{
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateWaitingListEntryCommandHandler(
        IWaitingListRepository waitingListRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _waitingListRepository = waitingListRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WaitingListEntryDto>> Handle(UpdateWaitingListEntryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (!Enum.TryParse<WaitingListPriority>(request.Priority, ignoreCase: true, out var priority))
                return Result<WaitingListEntryDto>.Failure("Priorité invalide.");

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<WaitingListEntryDto>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var entry = await _waitingListRepository.GetByIdAsync(request.Id, cancellationToken);
            if (entry == null || entry.ClinicId != clinic.Value)
                return Result<WaitingListEntryDto>.Failure("Entrée de liste d'attente introuvable.");

            entry.UpdateDetails(
                priority,
                request.PreferredDoctorId,
                string.IsNullOrWhiteSpace(request.DesiredTimeframe) ? null : request.DesiredTimeframe.Trim(),
                string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim());

            await _waitingListRepository.UpdateAsync(entry, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<WaitingListEntryDto>.Success(entry.ToDto());
        }
        catch (ArgumentException ex)
        {
            return Result<WaitingListEntryDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            return Result<WaitingListEntryDto>.Failure($"Erreur lors de la mise à jour de l'entrée de liste d'attente : {ex.Message}");
        }
    }
}
