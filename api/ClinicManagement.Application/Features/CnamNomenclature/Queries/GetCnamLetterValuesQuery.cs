using ClinicManagement.Application.Common.Exceptions;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.CnamNomenclature.Commands;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.CnamNomenclature.Queries;

// Read the valeurs de la lettre clé (VLC) — global reference data, any authenticated user (FR-5.2/5.3).
public class GetCnamLetterValuesQuery : IRequest<Result<IEnumerable<CnamLetterValueDto>>>
{
}

public class GetCnamLetterValuesQueryHandler
    : IRequestHandler<GetCnamLetterValuesQuery, Result<IEnumerable<CnamLetterValueDto>>>
{
    private readonly ICnamCatalogRepository _repository;
    private readonly ILogger<GetCnamLetterValuesQueryHandler> _logger;

    public GetCnamLetterValuesQueryHandler(
        ICnamCatalogRepository repository,
        ILogger<GetCnamLetterValuesQueryHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result<IEnumerable<CnamLetterValueDto>>> Handle(
        GetCnamLetterValuesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var values = (await _repository.GetAllLetterValuesAsync(cancellationToken))
                .Select(CnamEntryMapper.ToDto)
                .ToList();

            return Result<IEnumerable<CnamLetterValueDto>>.Success(values);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error retrieving CNAM letter values");
            return Result<IEnumerable<CnamLetterValueDto>>.Failure(
                "Erreur lors de la récupération des valeurs de la lettre clé.");
        }
    }
}
