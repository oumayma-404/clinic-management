using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Application.Features.WaitingList.Queries;

public class GetWaitingListQuery : IRequest<Result<PagedResult<WaitingListEntryDto>>>
{
    public bool ActiveOnly { get; set; } = true;

    /// <summary>1-based page and page size. Both null = every matching row.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>Free-text filter, matched in SQL across the whole clinic — never only the requested page.</summary>
    public string? SearchTerm { get; set; }
}

public class GetWaitingListQueryHandler : IRequestHandler<GetWaitingListQuery, Result<PagedResult<WaitingListEntryDto>>>
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

    public async Task<Result<PagedResult<WaitingListEntryDto>>> Handle(GetWaitingListQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<PagedResult<WaitingListEntryDto>>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var page = await _waitingListRepository.GetByClinicIdAsync(
                clinic.Value,
                request.ActiveOnly,
                request.SearchTerm,
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            return Result<PagedResult<WaitingListEntryDto>>.Success(page.Map(e => e.ToDto()));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PagedResult<WaitingListEntryDto>>.Failure($"Erreur lors de la récupération de la liste d'attente : {ex.Message}");
        }
    }
}
