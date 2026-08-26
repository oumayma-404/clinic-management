using MediatR;
using ClinicManagement.Application.Common.Exceptions;
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

    /// <summary>
    /// <c>false</c> reactivates instead of deactivating.
    ///
    /// ⚠️ <b>All three catalogue entities had an <c>Activate()</c> method nothing called.</b> A deactivated row
    /// stayed listed with only « Modifier », and an edit-save left <c>IsActive = false</c> — so a row switched off
    /// by mistake was switched off for ever, and the only route back was the database. A soft delete whose inverse
    /// is unreachable is a hard delete with extra steps.
    ///
    /// Defaults to <c>true</c> so the existing <c>DELETE</c> route keeps behaving exactly as it did.
    /// </summary>
    public bool Deactivate { get; set; } = true;
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

            if (request.Deactivate)
            {
                medication.Deactivate();
            }
            else
            {
                medication.Activate();
            }
            await _repository.UpdateAsync(medication, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "{Action} medication catalog entry {Id}", request.Deactivate ? "Deactivated" : "Reactivated", medication.Id);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error deactivating medication catalog entry {Id}", request.Id);
            return Result.Failure(request.Deactivate
                ? "Erreur lors de la désactivation du médicament."
                : "Erreur lors de la réactivation du médicament.");
        }
    }
}
