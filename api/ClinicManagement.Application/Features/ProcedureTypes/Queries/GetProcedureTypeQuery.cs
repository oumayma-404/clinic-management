using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
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
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetProcedureTypeQueryHandler> _logger;

    public GetProcedureTypeQueryHandler(
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetProcedureTypeQueryHandler> logger)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<ProcedureTypeDto>> Handle(GetProcedureTypeQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Verify the procedure type belongs to the caller's clinic (explicit scope, not just the
            // fail-open global filter). Return generic "not found" on mismatch — mirrors the siblings (AC-1).
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<ProcedureTypeDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var procedureType = await _procedureTypeRepository.GetByIdAsync(request.Id, cancellationToken);
            if (procedureType == null || procedureType.ClinicId != clinicResult.Value)
            {
                return Result<ProcedureTypeDto>.Failure("Type de procédure introuvable.");
            }

            var dto = new ProcedureTypeDto
            {
                Id = procedureType.Id,
                Name = procedureType.Name,
                DefaultDurationMinutes = procedureType.DefaultDurationMinutes,
                DefaultCost = procedureType.DefaultCost,
                ColorHex = procedureType.Color.Value,
                Description = procedureType.Description,
                ResultingCondition = procedureType.ResultingCondition?.ToString(),
                IsActive = procedureType.IsActive,
                CreatedAt = procedureType.CreatedAt,
                UpdatedAt = procedureType.UpdatedAt
            };

            return Result<ProcedureTypeDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error retrieving procedure type {ProcedureTypeId}", request.Id);
            return Result<ProcedureTypeDto>.Failure($"Error retrieving procedure type: {ex.Message}");
        }
    }
}


