using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Medications.Commands;

// Confirm the whole provisional medication dataset: clears the "à vérifier" flag on every catalog entry
// once an admin has reconciled the starter data. AdminOnly.
public class ConfirmMedicationDataCommand : IRequest<Result>
{
}

public class ConfirmMedicationDataCommandHandler : IRequestHandler<ConfirmMedicationDataCommand, Result>
{
    private readonly IMedicationCatalogRepository _repository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConfirmMedicationDataCommandHandler> _logger;

    public ConfirmMedicationDataCommandHandler(
        IMedicationCatalogRepository repository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<ConfirmMedicationDataCommandHandler> logger)
    {
        _repository = repository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ConfirmMedicationDataCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Authoritative tenant guard on a BULK write. There is no id to check here — the repository read
            // goes through the fail-open EF query filter, so with no clinic_id claim in scope this loop would
            // confirm EVERY clinic's provisional rows in one call (audit § 2, finding 10). Filtering the
            // returned set is the equivalent of the single-row check the Update/Deactivate commands do.
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var medications = await _repository.GetAllAsync(includeInactive: true, cancellationToken);
            foreach (var medication in medications.Where(m => m.IsProvisional && m.ClinicId == clinicResult.Value))
            {
                medication.Confirm();
                await _repository.UpdateAsync(medication, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Confirmed medication catalog (cleared provisional flags)");
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming medication catalog");
            return Result.Failure("Erreur lors de la confirmation du catalogue des médicaments.");
        }
    }
}
