using MediatR;
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
    private readonly ILogger<GetProcedureTypesQueryHandler> _logger;

    public GetProcedureTypesQueryHandler(
        IProcedureTypeRepository procedureTypeRepository,
        ILogger<GetProcedureTypesQueryHandler> logger)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _logger = logger;
    }

    public async Task<Result<IEnumerable<ProcedureTypeDto>>> Handle(GetProcedureTypesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            IEnumerable<Domain.Entities.ProcedureType> procedureTypes;

            if (request.IncludeInactive)
            {
                procedureTypes = await _procedureTypeRepository.GetAllAsync(cancellationToken);
            }
            else
            {
                procedureTypes = await _procedureTypeRepository.GetActiveAsync(cancellationToken);
            }

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


