using ClinicManagement.Application.Common.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Application.Features.DentalActs.Queries;

/// <summary>List the global dental act catalog, optionally filtered by free-text query and/or category.</summary>
public class GetDentalActsQuery : IRequest<Result<PagedResult<DentalActDto>>>
{
    /// <summary>1-based page and page size. Both null = every matching row.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    public string? Q { get; set; }
    public string? Category { get; set; }
    public bool IncludeInactive { get; set; }
}

public class GetDentalActsQueryHandler : IRequestHandler<GetDentalActsQuery, Result<PagedResult<DentalActDto>>>
{
    private readonly IDentalActCodeRepository _repository;
    private readonly ILogger<GetDentalActsQueryHandler> _logger;

    public GetDentalActsQueryHandler(IDentalActCodeRepository repository, ILogger<GetDentalActsQueryHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result<PagedResult<DentalActDto>>> Handle(GetDentalActsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Category and `Q` are filtered in the repository: an in-memory filter and a SQL page cannot coexist.
            var page = await _repository.GetAllAsync(
                request.IncludeInactive,
                request.Category,
                request.Q,
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            return Result<PagedResult<DentalActDto>>.Success(page.Map(a => a.ToDto()));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error listing dental acts");
            return Result<PagedResult<DentalActDto>>.Failure("Erreur lors du chargement du catalogue des actes.");
        }
    }
}
