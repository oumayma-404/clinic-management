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
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConfirmCnamDataCommandHandler> _logger;

    public ConfirmCnamDataCommandHandler(
        ICnamCatalogRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<ConfirmCnamDataCommandHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ConfirmCnamDataCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var entries = await _repository.GetAllAsync(includeInactive: true, cancellationToken);
            foreach (var entry in entries.Where(e => e.IsProvisional))
            {
                entry.Confirm();
                await _repository.UpdateAsync(entry, cancellationToken);
            }

            var values = await _repository.GetAllLetterValuesAsync(cancellationToken);
            foreach (var value in values.Where(v => v.IsProvisional))
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
