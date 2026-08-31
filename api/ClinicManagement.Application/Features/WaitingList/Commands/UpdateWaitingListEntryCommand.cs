using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
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
    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the user was
    /// editing; <c>0</c> means « not supplied » and skips the check (see <c>IUnitOfWork.SetExpectedVersion</c>).
    /// </summary>
    public uint Version { get; set; }
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

            if (WaitingListLimits.Refuse(request.DesiredTimeframe, request.Note) is { } tooLong)
                return Result<WaitingListEntryDto>.Failure(tooLong);

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

            // Band B — validated against the copy the USER was editing, not the row this handler just read.
            _unitOfWork.SetExpectedVersion(entry, request.Version);

            await _waitingListRepository.UpdateAsync(entry, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<WaitingListEntryDto>.Success(entry.ToDto());
        }
        catch (ArgumentException ex)
        {
            return Result<WaitingListEntryDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<WaitingListEntryDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
