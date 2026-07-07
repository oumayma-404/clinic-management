using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.ProcedureTypes.Queries;

public class GetProcedureTypeQuery : IRequest<Result<ProcedureTypeDto>>
{
    public Guid Id { get; set; }
}

public class GetProcedureTypeQueryHandler : IRequestHandler<GetProcedureTypeQuery, Result<ProcedureTypeDto>>
{
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ILogger<GetProcedureTypeQueryHandler> _logger;

    public GetProcedureTypeQueryHandler(
        IProcedureTypeRepository procedureTypeRepository,
        ILogger<GetProcedureTypeQueryHandler> logger)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _logger = logger;
    }

    public async Task<Result<ProcedureTypeDto>> Handle(GetProcedureTypeQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var procedureType = await _procedureTypeRepository.GetByIdAsync(request.Id, cancellationToken);
            if (procedureType == null)
            {
                return Result<ProcedureTypeDto>.Failure("Procedure type not found");
            }

            var dto = new ProcedureTypeDto
            {
                Id = procedureType.Id,
                Name = procedureType.Name,
                DefaultDurationMinutes = procedureType.DefaultDurationMinutes,
                DefaultCost = procedureType.DefaultCost,
                ColorHex = procedureType.Color.Value,
                Description = procedureType.Description,
                IsActive = procedureType.IsActive,
                CreatedAt = procedureType.CreatedAt,
                UpdatedAt = procedureType.UpdatedAt
            };

            return Result<ProcedureTypeDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving procedure type {ProcedureTypeId}", request.Id);
            return Result<ProcedureTypeDto>.Failure($"Error retrieving procedure type: {ex.Message}");
        }
    }
}


