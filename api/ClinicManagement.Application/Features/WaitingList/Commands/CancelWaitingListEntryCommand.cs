using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.WaitingList.Commands;

/// <summary>
/// « Retirer de la liste » — the first caller <c>WaitingListEntry.Cancel()</c> has ever had (AC-25).
///
/// <para>The entity method and <c>WaitingListStatus.Cancelled</c> both shipped with no command, no route and no
/// affordance, so a patient who found a slot elsewhere or stopped waiting could only be <b>deleted</b> — which
/// destroys the record that they ever waited, and is the wrong answer to « pourquoi n'est-elle plus dans la
/// liste ? ». Cancelling keeps the row and says what happened to it.</para>
///
/// <para>⚠️ Deliberately a <b>separate command from delete</b>, not a flag on it: the two have opposite intents
/// (record the outcome vs. remove a mistaken entry) and the default list hides cancelled entries either way, so
/// folding them together would make the reversible one indistinguishable from the destructive one at the call
/// site. The <c>Promoted</c> refusal is the entity's — an entry that became a real appointment is not something
/// this can take back, and <c>Appointment.Cancel</c> is where that belongs.</para>
/// </summary>
public class CancelWaitingListEntryCommand : IRequest<Result<WaitingListEntryDto>>
{
    public Guid Id { get; set; }
}

public class CancelWaitingListEntryCommandHandler
    : IRequestHandler<CancelWaitingListEntryCommand, Result<WaitingListEntryDto>>
{
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public CancelWaitingListEntryCommandHandler(
        IWaitingListRepository waitingListRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _waitingListRepository = waitingListRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WaitingListEntryDto>> Handle(
        CancelWaitingListEntryCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<WaitingListEntryDto>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var entry = await _waitingListRepository.GetByIdAsync(request.Id, cancellationToken);
            if (entry == null || entry.ClinicId != clinic.Value)
                return Result<WaitingListEntryDto>.Failure("Entrée de liste d'attente introuvable.");

            entry.Cancel();

            await _waitingListRepository.UpdateAsync(entry, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<WaitingListEntryDto>.Success(entry.ToDto());
        }
        catch (InvalidOperationException ex)
        {
            // The entity's own French refusal for an already-promoted entry.
            return Result<WaitingListEntryDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<WaitingListEntryDto>.Failure(
                $"Erreur lors du retrait de l'entrée de liste d'attente : {ex.Message}");
        }
    }
}
