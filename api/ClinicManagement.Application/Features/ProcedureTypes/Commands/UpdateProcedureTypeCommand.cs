using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.ProcedureTypes.Commands;

public class UpdateProcedureTypeCommand : IRequest<Result<ProcedureTypeDto>>
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public int? DefaultDurationMinutes { get; set; }
    public decimal? DefaultCost { get; set; }
    public string? ColorHex { get; set; }
    public string? Description { get; set; }
    /// <summary>When provided, sets the resulting odontogram state ("" clears it).</summary>
    public string? ResultingCondition { get; set; }
}

public class UpdateProcedureTypeCommandHandler : IRequestHandler<UpdateProcedureTypeCommand, Result<ProcedureTypeDto>>
{
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateProcedureTypeCommandHandler> _logger;

    public UpdateProcedureTypeCommandHandler(
        IProcedureTypeRepository procedureTypeRepository,
        IAppointmentRepository appointmentRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<UpdateProcedureTypeCommandHandler> logger)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _appointmentRepository = appointmentRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ProcedureTypeDto>> Handle(UpdateProcedureTypeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var procedureType = await _procedureTypeRepository.GetByIdAsync(request.Id, cancellationToken);
            if (procedureType == null)
            {
                return Result<ProcedureTypeDto>.Failure("Type de procédure introuvable.");
            }

            // Explicit tenant check (defense-in-depth alongside the global query filter): a procedure
            // type from another clinic reads as "not found".
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<ProcedureTypeDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }
            if (procedureType.ClinicId != clinicResult.Value)
            {
                return Result<ProcedureTypeDto>.Failure("Type de procédure introuvable.");
            }

            // Update name if provided
            string? oldName = null;
            if (request.Name != null)
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return Result<ProcedureTypeDto>.Failure("Le nom ne peut pas être vide.");
                }

                // Check if name already exists (excluding current)
                var nameExists = await _procedureTypeRepository.ExistsByNameAsync(request.Name, request.Id, cancellationToken);
                if (nameExists)
                {
                    return Result<ProcedureTypeDto>.Failure($"A procedure type with the name '{request.Name}' already exists");
                }

                oldName = procedureType.Name;
                procedureType.UpdateName(request.Name);
            }

            // Update duration if provided
            if (request.DefaultDurationMinutes.HasValue)
            {
                if (request.DefaultDurationMinutes.Value <= 0)
                {
                    return Result<ProcedureTypeDto>.Failure("La durée par défaut doit être supérieure à 0.");
                }

                if (request.DefaultDurationMinutes.Value >= 480)
                {
                    return Result<ProcedureTypeDto>.Failure("La durée par défaut doit être inférieure à 480 minutes (8 heures).");
                }

                procedureType.UpdateDefaultDuration(request.DefaultDurationMinutes.Value);
            }

            // Update color if provided
            string? oldColorHex = null;
            if (request.ColorHex != null)
            {
                try
                {
                    oldColorHex = procedureType.Color.Value;
                    var color = new ColorHex(request.ColorHex);
                    procedureType.UpdateColor(color);
                }
                catch (ArgumentException ex)
                {
                    return Result<ProcedureTypeDto>.Failure(ex.Message);
                }
            }

            // Update default cost if provided in request
            _logger.LogInformation("UpdateProcedureType - DefaultCost in request: HasValue={HasValue}, Value={Value}", 
                request.DefaultCost.HasValue, request.DefaultCost.HasValue ? request.DefaultCost.Value : (decimal?)null);
            
            if (request.DefaultCost.HasValue)
            {
                if (request.DefaultCost.Value < 0)
                {
                    return Result<ProcedureTypeDto>.Failure("Le tarif par défaut ne peut pas être négatif.");
                }
                
                var oldCost = procedureType.DefaultCost;
                procedureType.UpdateDefaultCost(request.DefaultCost);
                _logger.LogInformation("UpdateProcedureType - Updated DefaultCost from {OldCost} to {NewCost}", 
                    oldCost, request.DefaultCost.Value);
            }
            else
            {
                _logger.LogInformation("UpdateProcedureType - DefaultCost not provided in request (HasValue=false)");
            }

            // Update description if provided
            if (request.Description != null)
            {
                procedureType.UpdateDescription(request.Description);
            }

            // Update resulting odontogram state if provided ("" clears it).
            if (request.ResultingCondition != null)
            {
                ToothCondition? rc = null;
                if (!string.IsNullOrWhiteSpace(request.ResultingCondition))
                {
                    if (!Enum.TryParse<ToothCondition>(request.ResultingCondition, ignoreCase: true, out var parsedRc))
                    {
                        return Result<ProcedureTypeDto>.Failure("État résultant invalide.");
                    }
                    rc = parsedRc;
                }
                procedureType.UpdateResultingCondition(rc);
            }

            // Update all appointments that use this procedure type if name or color changed
            bool needsAppointmentUpdate = (request.Name != null && oldName != request.Name) || 
                                         (request.ColorHex != null && oldColorHex != request.ColorHex);
            
            if (needsAppointmentUpdate)
            {
                var appointments = await _appointmentRepository.GetByProcedureTypeIdAsync(procedureType.Id, cancellationToken);
                var appointmentList = appointments.ToList();
                
                if (appointmentList.Any())
                {
                    foreach (var appointment in appointmentList)
                    {
                        appointment.SetProcedureType(
                            procedureType.Id,
                            appointment.ProcedureDurationMinutes,
                            procedureType.Color.Value);
                        await _appointmentRepository.UpdateAsync(appointment, cancellationToken);
                    }
                    
                    // Save appointment changes before saving procedure type
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    
                    _logger.LogInformation("Updated {Count} appointments using procedure type {ProcedureTypeId} (name: {NameChanged}, color: {ColorChanged})", 
                        appointmentList.Count, 
                        procedureType.Id,
                        request.Name != null && oldName != request.Name,
                        request.ColorHex != null && oldColorHex != request.ColorHex);
                }
            }

            await _procedureTypeRepository.UpdateAsync(procedureType, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated procedure type {ProcedureTypeId}", procedureType.Id);

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
            _logger.LogError(ex, "Error updating procedure type {ProcedureTypeId}", request.Id);
            return Result<ProcedureTypeDto>.Failure($"Error updating procedure type: {ex.Message}");
        }
    }
}

