using MediatR;
using ClinicManagement.Application.Common.Exceptions;
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
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeactivateCnamEntryCommandHandler> _logger;

    public DeactivateCnamEntryCommandHandler(
        ICnamCatalogRepository repository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeactivateCnamEntryCommandHandler> logger)
    {
        _repository = repository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(DeactivateCnamEntryCommand request, CancellationToken cancellationToken)
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

            var entry = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (entry is null || entry.ClinicId != clinicResult.Value)
            {
                return Result.Failure("Acte introuvable.");
            }

            entry.Deactivate();
            await _repository.UpdateAsync(entry, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deactivated CNAM catalog entry {Id}", entry.Id);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error deactivating CNAM catalog entry {Id}", request.Id);
            return Result.Failure("Erreur lors de la désactivation de l'acte.");
        }
    }
}
