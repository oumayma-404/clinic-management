using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Medications.Commands;

// Deactivate (soft-delete) a global medication catalog entry so it is excluded from active reads / the
// picker. AdminOnly. Unknown id → not-found failure.
public class DeactivateMedicationCommand : IRequest<Result>
{
    public Guid Id { get; set; }
}

public class DeactivateMedicationCommandHandler : IRequestHandler<DeactivateMedicationCommand, Result>
{
    private readonly IMedicationCatalogRepository _repository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeactivateMedicationCommandHandler> _logger;

    public DeactivateMedicationCommandHandler(
        IMedicationCatalogRepository repository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeactivateMedicationCommandHandler> logger)
    {
        _repository = repository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(DeactivateMedicationCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Authoritative tenant guard: resolve the caller's clinic from the DB rather than relying on the
            // EF global query filter, which is FAIL-OPEN — a token minted without a clinic_id claim leaves it
            // inactive, and this row could then be reached by id from another clinic (audit § 2, finding 10).
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var medication = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (medication is null || medication.ClinicId != clinicResult.Value)
            {
                return Result.Failure("Médicament introuvable.");
            }

            medication.Deactivate();
            await _repository.UpdateAsync(medication, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deactivated medication catalog entry {Id}", medication.Id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating medication catalog entry {Id}", request.Id);
            return Result.Failure("Erreur lors de la désactivation du médicament.");
        }
    }
}
