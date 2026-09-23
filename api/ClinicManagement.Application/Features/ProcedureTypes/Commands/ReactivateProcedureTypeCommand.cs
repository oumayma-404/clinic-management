using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.ProcedureTypes.Commands;

/// <summary>
/// Bring an archived act back into « Mes actes » (I1). Archiving had no inverse: <c>ProcedureType.Activate()</c> had
/// no caller, the list never showed an archived row, and its name blocked re-creating it — a soft delete whose
/// inverse is unreachable is a hard delete with extra steps (the dental-acts catalogue's own note).
/// </summary>
public class ReactivateProcedureTypeCommand : IRequest<Result>
{
    public Guid Id { get; set; }
}

public class ReactivateProcedureTypeCommandHandler : IRequestHandler<ReactivateProcedureTypeCommand, Result>
{
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReactivateProcedureTypeCommandHandler> _logger;

    public ReactivateProcedureTypeCommandHandler(
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<ReactivateProcedureTypeCommandHandler> logger)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ReactivateProcedureTypeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var procedureType = await _procedureTypeRepository.GetByIdAsync(request.Id, cancellationToken);
            if (procedureType == null || procedureType.ClinicId != clinicResult.Value)
            {
                return Result.Failure("Type de procédure introuvable.");
            }

            procedureType.Activate();
            await _procedureTypeRepository.UpdateAsync(procedureType, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reactivating procedure type {ProcedureTypeId}", request.Id);
            return Result.Failure("Erreur lors de la réactivation de l'acte.");
        }
    }
}
