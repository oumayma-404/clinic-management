using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.CnamNomenclature.Queries;

// Read the curated CNAM dental nomenclature, optionally filtered by free text and/or category.
// Reference data shared across clinics, so this query is NOT clinic-scoped (unlike ProcedureTypes);
// the controller still requires an authenticated user.
public class GetCnamNomenclatureQuery : IRequest<Result<IEnumerable<CnamNomenclatureEntryDto>>>
{
    public string? Q { get; set; }
    public string? Category { get; set; }
}

public class GetCnamNomenclatureQueryHandler
    : IRequestHandler<GetCnamNomenclatureQuery, Result<IEnumerable<CnamNomenclatureEntryDto>>>
{
    private readonly ICnamNomenclatureProvider _provider;
    private readonly ILogger<GetCnamNomenclatureQueryHandler> _logger;

    public GetCnamNomenclatureQueryHandler(
        ICnamNomenclatureProvider provider,
        ILogger<GetCnamNomenclatureQueryHandler> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public Task<Result<IEnumerable<CnamNomenclatureEntryDto>>> Handle(
        GetCnamNomenclatureQuery request, CancellationToken cancellationToken)
    {
        try
        {
            IEnumerable<CnamNomenclatureEntryDto> entries = _provider.GetAll();

            if (!string.IsNullOrWhiteSpace(request.Category))
            {
                var category = request.Category.Trim();
                entries = entries.Where(e =>
                    string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(request.Q))
            {
                var q = request.Q.Trim();
                entries = entries.Where(e =>
                    e.CodeActe.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    e.DesignationFr.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    e.LettreCle.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult(
                Result<IEnumerable<CnamNomenclatureEntryDto>>.Success(entries.ToList()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving CNAM nomenclature");
            return Task.FromResult(
                Result<IEnumerable<CnamNomenclatureEntryDto>>.Failure(
                    $"Error retrieving CNAM nomenclature: {ex.Message}"));
        }
    }
}
