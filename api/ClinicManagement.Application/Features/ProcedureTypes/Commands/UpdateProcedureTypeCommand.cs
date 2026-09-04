using System.Text.Json.Serialization;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
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

    /// <summary>
    /// Band A — <b>tri-state, and it has to be, because a price has no empty string.</b> Omit the key to leave the
    /// tarif alone; send an explicit <c>null</c> to un-price the act; send a figure to set it.
    ///
    /// <para>⚠️ It used to be a plain <c>decimal?</c> tested with <c>HasValue</c>, which conflated « not supplied »
    /// with « clear it » — so <b>an act could never be un-priced anywhere in the product</b>: clearing the field
    /// reported success and the old tarif came back on reload. The nullable text fields beside it get the same
    /// distinction for free from <c>""</c>; a number needs <see cref="DefaultCostSpecified"/> to say it.</para>
    /// </summary>
    public decimal? DefaultCost
    {
        get => _defaultCost;
        set { _defaultCost = value; DefaultCostSpecified = true; }
    }
    private decimal? _defaultCost;

    /// <summary>True once the body carried a <c>defaultCost</c> key at all — including an explicit null.</summary>
    [JsonIgnore]
    public bool DefaultCostSpecified { get; private set; }
    public string? ColorHex { get; set; }
    public string? Description { get; set; }
    /// <summary>
    /// Clinical discipline. <b>Tri-state, like every other field here</b>: omit to leave it alone, <c>""</c> to
    /// unfile the act, a label to file it. Canonicalised by the entity, so an unknown label is a new category.
    /// </summary>
    public string? Category { get; set; }
    /// <summary>When provided, sets the resulting odontogram state ("" clears it).</summary>
    public string? ResultingCondition { get; set; }

    /// <summary>
    /// The act's suggested clinical steps. <b>Tri-state, and a list gets the distinction for free</b>: omit the
    /// key to leave the template alone, send <c>[]</c> to clear it (« cet acte se fait en une séance », a real
    /// answer), send a list to replace it. No <c>Specified</c> companion is needed here, unlike
    /// <see cref="DefaultCost"/> — <c>null</c> and <c>[]</c> are already different JSON values.
    /// <para>
    /// Order is the list's own. Editing this touches <b>no</b> devis: a template is copied onto a plan line when
    /// the act is added, and the line owns its steps from then on — so re-wording a template can never rewrite
    /// the protocol of a bridge already under way.
    /// </para>
    /// </summary>
    public List<ProcedureStepTemplateDto>? DefaultSteps { get; set; }
    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the user was
    /// editing; <c>0</c> means « not supplied » and skips the check (see <c>IUnitOfWork.SetExpectedVersion</c>).
    /// </summary>
    public uint Version { get; set; }
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
                    return Result<ProcedureTypeDto>.Failure(ProcedureTypeRefusals.DuplicateName(request.Name));
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

            // Band A — keyed on Specified, not on HasValue, so an explicit null un-prices the act instead of
            // meaning « leave it alone ». See the property's own note.
            if (request.DefaultCostSpecified)
            {
                if (request.DefaultCost is < 0)
                {
                    return Result<ProcedureTypeDto>.Failure(ProcedureTypeRefusals.CostNegative);
                }

                if (request.DefaultCost > ProcedureTypeRefusals.MaxCost)
                {
                    return Result<ProcedureTypeDto>.Failure(ProcedureTypeRefusals.CostTooLarge);
                }

                procedureType.UpdateDefaultCost(request.DefaultCost);
            }

            // Update description if provided
            if (request.Description != null)
            {
                procedureType.UpdateDescription(request.Description);
            }

            // Update the discipline if provided ("" unfiles the act) — the same null-means-unchanged /
            // empty-means-clear tri-state every other field of this command uses.
            if (request.Category != null)
            {
                procedureType.UpdateCategory(request.Category);
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

            // The step template — null leaves it alone, [] clears it. The entity validates label, length,
            // duration band and count; its ArgumentException carries the French sentence, so it is translated
            // here rather than duplicated.
            if (request.DefaultSteps != null)
            {
                try
                {
                    procedureType.SetDefaultSteps(request.DefaultSteps
                        .Select(s => new ProcedureStepTemplate(s.Label, s.DurationMinutes, s.MinDaysAfterPrevious)));
                }
                catch (ArgumentException ex)
                {
                    return Result<ProcedureTypeDto>.Failure(ex.Message);
                }
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
                        // Re-snapshot, never re-set: SetProcedureType now means "this visit has exactly this one
                        // act", so calling it here would delete the other acts of every multi-act séance that
                        // happens to use the renamed procedure.
                        appointment.RefreshProcedureSnapshot(
                            procedureType.Id,
                            procedureType.Name,
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

            // Band B — validated against the copy the USER was editing, not the row this handler just read.
            _unitOfWork.SetExpectedVersion(procedureType, request.Version);

            await _procedureTypeRepository.UpdateAsync(procedureType, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated procedure type {ProcedureTypeId}", procedureType.Id);

            return Result<ProcedureTypeDto>.Success(procedureType.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error updating procedure type {ProcedureTypeId}", request.Id);
            // No `ex.Message`: an EF/Npgsql sentence is English machine text and this string is rendered verbatim.
            return Result<ProcedureTypeDto>.Failure("Erreur lors de la mise à jour de l'acte.");
        }
    }
}

