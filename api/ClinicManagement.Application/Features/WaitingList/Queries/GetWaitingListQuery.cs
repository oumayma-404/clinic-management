using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.WaitingList.Queries;

public class GetWaitingListQuery : IRequest<Result<IEnumerable<WaitingListEntryDto>>>
{
    public bool ActiveOnly { get; set; } = true;
}

public class GetWaitingListQueryHandler : IRequestHandler<GetWaitingListQuery, Result<IEnumerable<WaitingListEntryDto>>>
{
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetWaitingListQueryHandler(
        IWaitingListRepository waitingListRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _waitingListRepository = waitingListRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IEnumerable<WaitingListEntryDto>>> Handle(GetWaitingListQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<IEnumerable<WaitingListEntryDto>>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var entries = await _waitingListRepository.GetByClinicIdAsync(clinic.Value, request.ActiveOnly, cancellationToken);
            return Result<IEnumerable<WaitingListEntryDto>>.Success(entries.Select(e => e.ToDto()));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<WaitingListEntryDto>>.Failure($"Erreur lors de la récupération de la liste d'attente : {ex.Message}");
        }
    }
}
