using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.DentalActs.Commands;

/// <summary>Update a dental act catalog entry. AdminOnly (controller-enforced).</summary>
public class UpdateDentalActCommand : IRequest<Result<DentalActDto>>
{
    public Guid Id { get; set; }
    public string CodeActe { get; set; } = string.Empty;
    public string DesignationFr { get; set; } = string.Empty;
    public string LettreCle { get; set; } = "D";
    public decimal? Coefficient { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal? DefaultFee { get; set; }
    public bool RequiresAccordPrealable { get; set; }
}

public class UpdateDentalActCommandHandler : IRequestHandler<UpdateDentalActCommand, Result<DentalActDto>>
{
    private readonly IDentalActCodeRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateDentalActCommandHandler> _logger;

    public UpdateDentalActCommandHandler(
        IDentalActCodeRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<UpdateDentalActCommandHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<DentalActDto>> Handle(UpdateDentalActCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var act = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (act == null)
            {
                return Result<DentalActDto>.Failure("Acte introuvable.");
            }

            if (await _repository.CodeActeExistsAsync(request.CodeActe, request.Id, cancellationToken))
            {
                return Result<DentalActDto>.Failure($"Un acte avec le code « {request.CodeActe.Trim()} » existe déjà.");
            }

            try
            {
                act.Update(
                    request.CodeActe,
                    request.DesignationFr,
                    string.IsNullOrWhiteSpace(request.LettreCle) ? "D" : request.LettreCle,
                    request.Coefficient,
                    request.Category,
                    request.DefaultFee,
                    request.RequiresAccordPrealable);
            }
            catch (ArgumentException ex)
            {
                return Result<DentalActDto>.Failure(ex.Message);
            }

            await _repository.UpdateAsync(act, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<DentalActDto>.Success(act.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error updating dental act {Id}", request.Id);
            return Result<DentalActDto>.Failure("Erreur lors de la mise à jour de l'acte.");
        }
    }
}
