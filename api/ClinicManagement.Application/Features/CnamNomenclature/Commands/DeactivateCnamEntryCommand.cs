using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.CnamNomenclature.Commands;

// Deactivate (soft-delete) a global CNAM catalog entry (FR-5.1) so it is excluded from active reads.
// AdminOnly. Unknown id → not-found failure.
public class DeactivateCnamEntryCommand : IRequest<Result>
{
    public Guid Id { get; set; }
}

public class DeactivateCnamEntryCommandHandler : IRequestHandler<DeactivateCnamEntryCommand, Result>
{
    private readonly ICnamCatalogRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeactivateCnamEntryCommandHandler> _logger;

    public DeactivateCnamEntryCommandHandler(
        ICnamCatalogRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<DeactivateCnamEntryCommandHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(DeactivateCnamEntryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (entry is null)
            {
                return Result.Failure("Acte introuvable.");
            }

            entry.Deactivate();
            await _repository.UpdateAsync(entry, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deactivated CNAM catalog entry {Id}", entry.Id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating CNAM catalog entry {Id}", request.Id);
            return Result.Failure("Erreur lors de la désactivation de l'acte.");
        }
    }
}
