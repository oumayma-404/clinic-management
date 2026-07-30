using ClinicManagement.Application.Common.Exceptions;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Medications.Commands;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Application.Features.Medications.Queries;

// Read the DB-backed medication catalog, optionally filtered by free text. GLOBAL reference data shared
// across clinics, so this query is NOT clinic-scoped; the controller still requires an authenticated user.
// Active-only by default; the admin screen passes IncludeInactive = true to also see deactivated rows.
public class GetMedicationsQuery : IRequest<Result<PagedResult<MedicationDto>>>
{
    /// <summary>1-based page and page size. Both null = every matching row.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    public string? Q { get; set; }
    public bool IncludeInactive { get; set; }
}

public class GetMedicationsQueryHandler
    : IRequestHandler<GetMedicationsQuery, Result<PagedResult<MedicationDto>>>
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

    public async Task<Result<PagedResult<MedicationDto>>> Handle(
        GetMedicationsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // `Q` is a repository argument now. It matched the mapped DTOs here, including their flattened
            // `Dcis`, which is the clause that made an in-memory filter feel necessary — in SQL it is an EXISTS
            // over the ingredient child rows, and it has to be, or a page could never be searched by molecule.
            var page = await _repository.GetAllAsync(
                request.IncludeInactive,
                request.Q,
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            return Result<PagedResult<MedicationDto>>.Success(page.Map(MedicationMapper.ToDto));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error retrieving medication catalog");
            return Result<PagedResult<MedicationDto>>.Failure(
                "Erreur lors de la récupération du catalogue des médicaments.");
        }
    }
}
