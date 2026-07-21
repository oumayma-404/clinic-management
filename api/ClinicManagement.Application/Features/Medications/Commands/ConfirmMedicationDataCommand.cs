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
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConfirmMedicationDataCommandHandler> _logger;

    public ConfirmMedicationDataCommandHandler(
        IMedicationCatalogRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<ConfirmMedicationDataCommandHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ConfirmMedicationDataCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var medications = await _repository.GetAllAsync(includeInactive: true, cancellationToken);
            foreach (var medication in medications.Where(m => m.IsProvisional))
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
