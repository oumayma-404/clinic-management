using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.DentalActs.Commands;

/// <summary>Clear the provisional "à vérifier" flag on every dental act. AdminOnly (controller-enforced).</summary>
public class ConfirmDentalActsCommand : IRequest<Result>
{
}

public class ConfirmDentalActsCommandHandler : IRequestHandler<ConfirmDentalActsCommand, Result>
{
    private readonly IDentalActCodeRepository _repository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConfirmDentalActsCommandHandler> _logger;

    public ConfirmDentalActsCommandHandler(
        IDentalActCodeRepository repository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<ConfirmDentalActsCommandHandler> logger)
    {
        _repository = repository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ConfirmDentalActsCommand request, CancellationToken cancellationToken)
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

            var provisional = await _repository.GetProvisionalAsync(cancellationToken);
            foreach (var act in provisional.Where(a => a.ClinicId == clinicResult.Value))
            {
                act.Confirm();
                await _repository.UpdateAsync(act, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error confirming dental act catalog");
            return Result.Failure("Erreur lors de la confirmation du catalogue.");
        }
    }
}
