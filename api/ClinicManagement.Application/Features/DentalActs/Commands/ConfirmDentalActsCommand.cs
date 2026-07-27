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
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConfirmDentalActsCommandHandler> _logger;

    public ConfirmDentalActsCommandHandler(
        IDentalActCodeRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<ConfirmDentalActsCommandHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ConfirmDentalActsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var provisional = await _repository.GetProvisionalAsync(cancellationToken);
            foreach (var act in provisional)
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
