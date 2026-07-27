using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.DentalActs.Commands;

/// <summary>Soft-deactivate a dental act catalog entry. AdminOnly (controller-enforced).</summary>
public class DeactivateDentalActCommand : IRequest<Result>
{
    public Guid Id { get; set; }
}

public class DeactivateDentalActCommandHandler : IRequestHandler<DeactivateDentalActCommand, Result>
{
    private readonly IDentalActCodeRepository _repository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeactivateDentalActCommandHandler> _logger;

    public DeactivateDentalActCommandHandler(
        IDentalActCodeRepository repository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeactivateDentalActCommandHandler> logger)
    {
        _repository = repository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(DeactivateDentalActCommand request, CancellationToken cancellationToken)
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

            var act = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (act == null || act.ClinicId != clinicResult.Value)
            {
                return Result.Failure("Acte introuvable.");
            }

            act.Deactivate();
            await _repository.UpdateAsync(act, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating dental act {Id}", request.Id);
            return Result.Failure("Erreur lors de la désactivation de l'acte.");
        }
    }
}
