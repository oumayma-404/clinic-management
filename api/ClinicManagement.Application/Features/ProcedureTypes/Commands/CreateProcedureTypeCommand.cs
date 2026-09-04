using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
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
    /// <summary>
    /// Clinical discipline to file the act under; null/blank = unfiled. Accepted as typed and canonicalised by
    /// the entity, so an unrecognised label is a new category rather than a validation failure.
    /// </summary>
    public string? Category { get; set; }
    /// <summary>Resulting odontogram state (ToothCondition name) for acts of this procedure; null/empty = none.</summary>
    public string? ResultingCondition { get; set; }

    /// <summary>
    /// The act's suggested clinical steps — « Préparation, Empreinte, Scellement définitif ». Optional and
    /// empty by default: an act done in one séance has none, which is most of them.
    /// <para>
    /// Accepted here as well as on the update command deliberately. The catalogue form posts one body, and an
    /// act creatable only without its protocol would send every practice through create-then-edit — the kind of
    /// asymmetry that leaves one door validating what the other does not.
    /// </para>
    /// </summary>
    public List<ProcedureStepTemplateDto>? DefaultSteps { get; set; }
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
                return Result<ProcedureTypeDto>.Failure("Le nom est requis.");
            }

            // Resolve the caller's clinic — the new procedure type is scoped to it.
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<ProcedureTypeDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            // Check if name already exists. The global query filter scopes this to the caller's clinic,
            // so uniqueness is enforced per-clinic (matching the composite unique index).
            var nameExists = await _procedureTypeRepository.ExistsByNameAsync(request.Name, null, cancellationToken);
            if (nameExists)
            {
                return Result<ProcedureTypeDto>.Failure(ProcedureTypeRefusals.DuplicateName(request.Name));
            }

            // Validate duration
            if (request.DefaultDurationMinutes <= 0)
            {
                return Result<ProcedureTypeDto>.Failure("La durée par défaut doit être supérieure à 0.");
            }

            if (request.DefaultDurationMinutes >= 480)
            {
                return Result<ProcedureTypeDto>.Failure("La durée par défaut doit être inférieure à 480 minutes (8 heures).");
            }

            // Validate default cost if provided
            if (request.DefaultCost.HasValue && request.DefaultCost.Value < 0)
            {
                return Result<ProcedureTypeDto>.Failure(ProcedureTypeRefusals.CostNegative);
            }

            // ⚠️ The ceiling was on the update path only, though ProcedureTypeRefusals' own docstring claims it
            // was missing from « both » and had been put right. Creating an act at 999 999 999 999 999 999 was
            // accepted here and refused by PostgreSQL, reaching the dentist as an English EF sentence.
            if (request.DefaultCost > ProcedureTypeRefusals.MaxCost)
            {
                return Result<ProcedureTypeDto>.Failure(ProcedureTypeRefusals.CostTooLarge);
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
            // Named arguments: `description` and `category` are adjacent nullable strings, and passing them
            // positionally is how the catalogue seed spent its whole life writing the category into the
            // description. See ProcedureTypeCatalogSeed.CreateFor.
            var procedureType = new ProcedureType(
                id: Guid.NewGuid(),
                clinicId: clinicId,
                name: request.Name,
                defaultDurationMinutes: request.DefaultDurationMinutes,
                color: color,
                description: request.Description,
                defaultCost: request.DefaultCost,
                resultingCondition: resultingCondition,
                category: request.Category,
                defaultSteps: request.DefaultSteps?
                    .Select(x => new ProcedureStepTemplate(x.Label, x.DurationMinutes, x.MinDaysAfterPrevious)));

            await _procedureTypeRepository.AddAsync(procedureType, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created procedure type {ProcedureTypeId} with name {Name}", procedureType.Id, procedureType.Name);

            return Result<ProcedureTypeDto>.Success(procedureType.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error creating procedure type");
            // No `ex.Message`: an EF/Npgsql sentence is English machine text and this string is rendered verbatim.
            return Result<ProcedureTypeDto>.Failure("Erreur lors de la création de l'acte.");
        }
    }
}

