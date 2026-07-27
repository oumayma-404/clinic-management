using ClinicManagement.Application.Common.Exceptions;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Medications.Commands;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Medications.Queries;

// Read the DB-backed medication catalog, optionally filtered by free text. GLOBAL reference data shared
// across clinics, so this query is NOT clinic-scoped; the controller still requires an authenticated user.
// Active-only by default; the admin screen passes IncludeInactive = true to also see deactivated rows.
public class GetMedicationsQuery : IRequest<Result<IEnumerable<MedicationDto>>>
{
    public string? Q { get; set; }
    public bool IncludeInactive { get; set; }
}

public class GetMedicationsQueryHandler
    : IRequestHandler<GetMedicationsQuery, Result<IEnumerable<MedicationDto>>>
{
    private readonly IMedicationCatalogRepository _repository;
    private readonly ILogger<GetMedicationsQueryHandler> _logger;

    public GetMedicationsQueryHandler(
        IMedicationCatalogRepository repository,
        ILogger<GetMedicationsQueryHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result<IEnumerable<MedicationDto>>> Handle(
        GetMedicationsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var medications = (await _repository.GetAllAsync(request.IncludeInactive, cancellationToken))
                .Select(MedicationMapper.ToDto)
                .AsEnumerable();

            if (!string.IsNullOrWhiteSpace(request.Q))
            {
                var q = request.Q.Trim();
                medications = medications.Where(m =>
                    m.BrandName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    m.Form.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    m.Strength.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    m.Dcis.Any(d => d.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }

            return Result<IEnumerable<MedicationDto>>.Success(medications.ToList());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error retrieving medication catalog");
            return Result<IEnumerable<MedicationDto>>.Failure(
                "Erreur lors de la récupération du catalogue des médicaments.");
        }
    }
}
