using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
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
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeactivateDentalActCommandHandler> _logger;

    public DeactivateDentalActCommandHandler(
        IDentalActCodeRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<DeactivateDentalActCommandHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(DeactivateDentalActCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var act = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (act == null)
            {
                return Result.Failure("Acte introuvable.");
            }

            act.Deactivate();
            await _repository.UpdateAsync(act, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error deactivating dental act {Id}", request.Id);
            return Result.Failure("Erreur lors de la désactivation de l'acte.");
        }
    }
}
