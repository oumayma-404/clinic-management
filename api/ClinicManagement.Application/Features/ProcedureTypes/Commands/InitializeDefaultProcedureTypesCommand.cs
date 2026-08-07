using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.ProcedureTypes.Commands;

/// <summary>
/// Backfill the current clinic's procedure menu with the starter set of common Tunisian dental procedures
/// (idempotent — skips any whose name already exists). Returns the number of procedures actually added.
/// </summary>
public class InitializeDefaultProcedureTypesCommand : IRequest<Result<int>>
{
}

public class InitializeDefaultProcedureTypesCommandHandler : IRequestHandler<InitializeDefaultProcedureTypesCommand, Result<int>>
{
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<InitializeDefaultProcedureTypesCommandHandler> _logger;

    public InitializeDefaultProcedureTypesCommandHandler(
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<InitializeDefaultProcedureTypesCommandHandler> logger)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(InitializeDefaultProcedureTypesCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<int>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var existing = (await _procedureTypeRepository.GetFilteredAsync(
                clinicResult.Value, includeInactive: true, cancellationToken: cancellationToken)).Items;
            var existingNames = new HashSet<string>(existing.Select(p => p.Name.Trim()), StringComparer.OrdinalIgnoreCase);

            var added = 0;
            foreach (var procedureType in ProcedureTypeCatalogSeed.CreateFor(clinicResult.Value))
            {
                if (existingNames.Contains(procedureType.Name.Trim()))
                {
                    continue;
                }
                await _procedureTypeRepository.AddAsync(procedureType, cancellationToken);
                added++;
            }

            if (added > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation("Seeded {Count} default procedure types for clinic {ClinicId}", added, clinicResult.Value);
            return Result<int>.Success(added);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error seeding default procedure types");
            return Result<int>.Failure("Erreur lors du chargement des actes courants.");
        }
    }
}
