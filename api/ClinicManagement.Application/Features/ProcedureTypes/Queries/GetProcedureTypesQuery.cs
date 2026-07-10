using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.ProcedureTypes.Queries;

public class GetProcedureTypesQuery : IRequest<Result<IEnumerable<ProcedureTypeDto>>>
{
    public bool IncludeInactive { get; set; } = false;
}

public class GetProcedureTypesQueryHandler : IRequestHandler<GetProcedureTypesQuery, Result<IEnumerable<ProcedureTypeDto>>>
{
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetProcedureTypesQueryHandler> _logger;

    public GetProcedureTypesQueryHandler(
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetProcedureTypesQueryHandler> logger)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<IEnumerable<ProcedureTypeDto>>> Handle(GetProcedureTypesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Scope explicitly to the caller's clinic so isolation does not hinge on the fail-open global
            // filter (defense-in-depth; the sibling Update/Delete/Create handlers scope the same way).
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<IEnumerable<ProcedureTypeDto>>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }
            var clinicId = clinicResult.Value;

            IEnumerable<Domain.Entities.ProcedureType> procedureTypes;

            if (request.IncludeInactive)
            {
                procedureTypes = await _procedureTypeRepository.GetAllAsync(cancellationToken);
            }
            else
            {
                procedureTypes = await _procedureTypeRepository.GetActiveAsync(cancellationToken);
            }

            procedureTypes = procedureTypes.Where(pt => pt.ClinicId == clinicId);

            var dtos = procedureTypes.Select(pt => new ProcedureTypeDto
            {
                Id = pt.Id,
                Name = pt.Name,
                DefaultDurationMinutes = pt.DefaultDurationMinutes,
                DefaultCost = pt.DefaultCost,
                ColorHex = pt.Color.Value,
                Description = pt.Description,
                IsActive = pt.IsActive,
                CreatedAt = pt.CreatedAt,
                UpdatedAt = pt.UpdatedAt
            }).ToList();

            return Result<IEnumerable<ProcedureTypeDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving procedure types");
            return Result<IEnumerable<ProcedureTypeDto>>.Failure($"Error retrieving procedure types: {ex.Message}");
        }
    }
}


