using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.DentalActs.Queries;

/// <summary>List the global dental act catalog, optionally filtered by free-text query and/or category.</summary>
public class GetDentalActsQuery : IRequest<Result<List<DentalActDto>>>
{
    public string? Q { get; set; }
    public string? Category { get; set; }
    public bool IncludeInactive { get; set; }
}

public class GetDentalActsQueryHandler : IRequestHandler<GetDentalActsQuery, Result<List<DentalActDto>>>
{
    private readonly IDentalActCodeRepository _repository;
    private readonly ILogger<GetDentalActsQueryHandler> _logger;

    public GetDentalActsQueryHandler(IDentalActCodeRepository repository, ILogger<GetDentalActsQueryHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result<List<DentalActDto>>> Handle(GetDentalActsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var acts = await _repository.GetAllAsync(request.IncludeInactive, cancellationToken);
            var query = acts.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(request.Category))
            {
                query = query.Where(a => string.Equals(a.Category, request.Category, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(request.Q))
            {
                var term = request.Q.Trim();
                query = query.Where(a =>
                    a.CodeActe.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    a.DesignationFr.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            return Result<List<DentalActDto>>.Success(query.Select(a => a.ToDto()).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing dental acts");
            return Result<List<DentalActDto>>.Failure("Erreur lors du chargement du catalogue des actes.");
        }
    }
}
