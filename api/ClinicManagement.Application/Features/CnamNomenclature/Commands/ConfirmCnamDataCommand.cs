using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.CnamNomenclature.Commands;

// Confirm the whole provisional CNAM dataset (FR-5.1/5.2): clears the "à vérifier" flag on every catalog
// entry and every VLC value once an admin has reconciled them against the CNAM convention. AdminOnly.
public class ConfirmCnamDataCommand : IRequest<Result>
{
}

public class ConfirmCnamDataCommandHandler : IRequestHandler<ConfirmCnamDataCommand, Result>
{
    private readonly ICnamCatalogRepository _repository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConfirmCnamDataCommandHandler> _logger;

    public ConfirmCnamDataCommandHandler(
        ICnamCatalogRepository repository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<ConfirmCnamDataCommandHandler> logger)
    {
        _repository = repository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ConfirmCnamDataCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Authoritative tenant guard on a BULK write. There is no id to check here — the repository reads
            // go through the fail-open EF query filter, so with no clinic_id claim in scope these loops would
            // confirm EVERY clinic's provisional rows in one call (audit § 2, finding 10). Filtering the
            // returned sets is the equivalent of the single-row check the Update/Deactivate commands do.
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            // Unpaged: this confirms the whole catalog, so it must see every entry.
            var entries = (await _repository.GetAllAsync(
                includeInactive: true, cancellationToken: cancellationToken)).Items;
            foreach (var entry in entries.Where(e => e.IsProvisional && e.ClinicId == clinicResult.Value))
            {
                entry.Confirm();
                await _repository.UpdateAsync(entry, cancellationToken);
            }

            var values = await _repository.GetAllLetterValuesAsync(cancellationToken);
            foreach (var value in values.Where(v => v.IsProvisional && v.ClinicId == clinicResult.Value))
            {
                value.Confirm();
                await _repository.UpdateLetterValueAsync(value, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Confirmed CNAM catalog + VLC (cleared provisional flags)");
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error confirming CNAM data");
            return Result.Failure("Erreur lors de la confirmation des données CNAM.");
        }
    }
}
