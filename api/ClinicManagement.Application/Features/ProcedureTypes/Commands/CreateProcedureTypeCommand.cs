using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.ProcedureTypes.Commands;

public class CreateProcedureTypeCommand : IRequest<Result<ProcedureTypeDto>>
{
    public string Name { get; set; } = string.Empty;
    public int DefaultDurationMinutes { get; set; }
    public decimal? DefaultCost { get; set; }
    public string ColorHex { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Resulting odontogram state (ToothCondition name) for acts of this procedure; null/empty = none.</summary>
    public string? ResultingCondition { get; set; }
}

public class CreateProcedureTypeCommandHandler : IRequestHandler<CreateProcedureTypeCommand, Result<ProcedureTypeDto>>
{
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateProcedureTypeCommandHandler> _logger;

    public CreateProcedureTypeCommandHandler(
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CreateProcedureTypeCommandHandler> logger)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ProcedureTypeDto>> Handle(CreateProcedureTypeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Validate name is not empty
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Result<ProcedureTypeDto>.Failure("Name is required");
            }

            // Resolve the caller's clinic — the new procedure type is scoped to it.
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<ProcedureTypeDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }
            var clinicId = clinicResult.Value;

            // Check if name already exists. The global query filter scopes this to the caller's clinic,
            // so uniqueness is enforced per-clinic (matching the composite unique index).
            var nameExists = await _procedureTypeRepository.ExistsByNameAsync(request.Name, null, cancellationToken);
            if (nameExists)
            {
                return Result<ProcedureTypeDto>.Failure($"A procedure type with the name '{request.Name}' already exists");
            }

            // Validate duration
            if (request.DefaultDurationMinutes <= 0)
            {
                return Result<ProcedureTypeDto>.Failure("Default duration must be greater than 0");
            }

            if (request.DefaultDurationMinutes >= 480)
            {
                return Result<ProcedureTypeDto>.Failure("Default duration must be less than 480 minutes (8 hours)");
            }

            // Validate default cost if provided
            if (request.DefaultCost.HasValue && request.DefaultCost.Value < 0)
            {
                return Result<ProcedureTypeDto>.Failure("Default cost cannot be negative");
            }

            // Validate and create color
            ColorHex color;
            try
            {
                color = new ColorHex(request.ColorHex);
            }
            catch (ArgumentException ex)
            {
                return Result<ProcedureTypeDto>.Failure(ex.Message);
            }

            // Parse the optional resulting odontogram state.
            ToothCondition? resultingCondition = null;
            if (!string.IsNullOrWhiteSpace(request.ResultingCondition))
            {
                if (!Enum.TryParse<ToothCondition>(request.ResultingCondition, ignoreCase: true, out var rc))
                {
                    return Result<ProcedureTypeDto>.Failure("État résultant invalide.");
                }
                resultingCondition = rc;
            }

            // Create procedure type
            var procedureType = new ProcedureType(
                Guid.NewGuid(),
                clinicId,
                request.Name,
                request.DefaultDurationMinutes,
                color,
                request.Description,
                request.DefaultCost,
                resultingCondition);

            await _procedureTypeRepository.AddAsync(procedureType, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created procedure type {ProcedureTypeId} with name {Name}", procedureType.Id, procedureType.Name);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating procedure type");
            return Result<ProcedureTypeDto>.Failure($"Error creating procedure type: {ex.Message}");
        }
    }
}

