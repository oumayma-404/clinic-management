using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.ProcedureTypes.Commands;

/// <summary>
/// Sets an act's <b>material list</b> — the stock performing it draws down (AC-P4.14). The missing caller for
/// <c>ProcedureType.SetMaterials</c>: the entity method and the join table both shipped with P4's first half,
/// so until now a list could only be inserted straight into the database, which made act-driven consumption
/// (AC-P4.10) unreachable for every real clinic.
///
/// <para><b>Whole-list replacement</b>, matching the aggregate method and <c>TreatmentPlan.SetItems</c>: the
/// editor posts the list it is showing, so an empty list is a legitimate value meaning « this act consumes
/// nothing » (AC-P4.11) rather than « no change ». It is therefore a separate command from
/// <see cref="UpdateProcedureTypeCommand"/>, whose every field is null-means-unchanged — folding a
/// replace-semantics collection into a patch-semantics command is how a list gets silently wiped.</para>
/// </summary>
public class SetProcedureTypeMaterialsCommand : IRequest<Result<ProcedureTypeDto>>
{
    public Guid Id { get; set; }

    /// <summary>The complete list. Empty clears it — that is the opt-out (AC-P4.11), not a no-op.</summary>
    public List<MaterialLine> Materials { get; set; } = new();

    public class MaterialLine
    {
        public Guid StockItemId { get; set; }
        public int QuantityPerAct { get; set; }
    }
}

public class SetProcedureTypeMaterialsCommandHandler
    : IRequestHandler<SetProcedureTypeMaterialsCommand, Result<ProcedureTypeDto>>
{
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly IStockItemRepository _stockItemRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetProcedureTypeMaterialsCommandHandler> _logger;

    public SetProcedureTypeMaterialsCommandHandler(
        IProcedureTypeRepository procedureTypeRepository,
        IStockItemRepository stockItemRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<SetProcedureTypeMaterialsCommandHandler> logger)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _stockItemRepository = stockItemRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ProcedureTypeDto>> Handle(
        SetProcedureTypeMaterialsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<ProcedureTypeDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var procedureType = await _procedureTypeRepository.GetByIdAsync(request.Id, cancellationToken);
            if (procedureType == null || procedureType.ClinicId != clinicId)
            {
                return Result<ProcedureTypeDto>.Failure("Type de procédure introuvable.");
            }

            var lines = request.Materials ?? new List<SetProcedureTypeMaterialsCommand.MaterialLine>();

            // Validated here rather than left to the aggregate's ArgumentException, so the operator gets the
            // French message naming what is wrong instead of a generic 500-shaped failure.
            foreach (var line in lines)
            {
                if (line.QuantityPerAct <= 0)
                {
                    return Result<ProcedureTypeDto>.Failure("La quantité consommée doit être supérieure à 0.");
                }
            }

            if (lines.Select(l => l.StockItemId).Distinct().Count() != lines.Count)
            {
                return Result<ProcedureTypeDto>.Failure(
                    "Un même article ne peut apparaître qu'une fois dans la liste des consommables.");
            }

            // Every referenced item must be one of THIS clinic's (AC-P4.14 — material lists are per-clinic).
            // Without this check a crafted request could point an act at another clinic's stock item, and the
            // consumption service would then draw that clinic's stock down on fiche save.
            if (lines.Count > 0)
            {
                var clinicItemIds = (await _stockItemRepository.GetByClinicIdAsync(clinicId, cancellationToken: cancellationToken)).Items
                    .Select(i => i.Id)
                    .ToHashSet();

                if (lines.Any(l => !clinicItemIds.Contains(l.StockItemId)))
                {
                    return Result<ProcedureTypeDto>.Failure("Article de stock introuvable.");
                }
            }

            procedureType.SetMaterials(lines.Select(l => (l.StockItemId, l.QuantityPerAct)));

            await _procedureTypeRepository.UpdateAsync(procedureType, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Set {Count} material line(s) on procedure type {ProcedureTypeId}", lines.Count, procedureType.Id);

            return Result<ProcedureTypeDto>.Success(procedureType.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error setting materials for procedure type {ProcedureTypeId}", request.Id);
            return Result<ProcedureTypeDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
