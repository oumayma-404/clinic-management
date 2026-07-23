using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.DentalActs.Commands;

/// <summary>Create a global dental act catalog entry (chapitre DCH). AdminOnly (controller-enforced).</summary>
public class CreateDentalActCommand : IRequest<Result<DentalActDto>>
{
    public string CodeActe { get; set; } = string.Empty;
    public string DesignationFr { get; set; } = string.Empty;
    public string LettreCle { get; set; } = "D";
    public decimal? Coefficient { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal? DefaultFee { get; set; }
    public bool RequiresAccordPrealable { get; set; }
}

public class CreateDentalActCommandHandler : IRequestHandler<CreateDentalActCommand, Result<DentalActDto>>
{
    private readonly IDentalActCodeRepository _repository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateDentalActCommandHandler> _logger;

    public CreateDentalActCommandHandler(
        IDentalActCodeRepository repository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CreateDentalActCommandHandler> logger)
    {
        _repository = repository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<DentalActDto>> Handle(CreateDentalActCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.CodeActe))
            {
                return Result<DentalActDto>.Failure("Le code acte est obligatoire.");
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<DentalActDto>.Failure(clinicResult.Error ?? "Impossible de résoudre la clinique.");
            }

            // Existence check auto-scopes to the caller's clinic via the query filter → uniqueness is per-clinic (#5).
            if (await _repository.CodeActeExistsAsync(request.CodeActe, null, cancellationToken))
            {
                return Result<DentalActDto>.Failure($"Un acte avec le code « {request.CodeActe.Trim()} » existe déjà.");
            }

            DentalActCode act;
            try
            {
                act = new DentalActCode(
                    Guid.NewGuid(),
                    clinicResult.Value,
                    request.CodeActe,
                    request.DesignationFr,
                    request.Category,
                    string.IsNullOrWhiteSpace(request.LettreCle) ? "D" : request.LettreCle,
                    request.Coefficient,
                    request.DefaultFee,
                    request.RequiresAccordPrealable,
                    isProvisional: false);
            }
            catch (ArgumentException ex)
            {
                return Result<DentalActDto>.Failure(ex.Message);
            }

            await _repository.AddAsync(act, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created dental act {Id} ({Code})", act.Id, act.CodeActe);
            return Result<DentalActDto>.Success(act.ToDto());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating dental act");
            return Result<DentalActDto>.Failure("Erreur lors de la création de l'acte.");
        }
    }
}
