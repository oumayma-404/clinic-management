using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.CnamNomenclature.Commands;

// Create a global CNAM catalog entry (FR-5.1). AdminOnly (controller-enforced). New entries seed the
// provisional "à vérifier" flag. Duplicate code acte is rejected with a French message.
public class CreateCnamEntryCommand : IRequest<Result<CnamNomenclatureEntryDto>>
{
    public string CodeActe { get; set; } = string.Empty;
    public string DesignationFr { get; set; } = string.Empty;
    public string LettreCle { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public string Category { get; set; } = string.Empty;
}

public class CreateCnamEntryCommandHandler : IRequestHandler<CreateCnamEntryCommand, Result<CnamNomenclatureEntryDto>>
{
    private readonly ICnamCatalogRepository _repository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateCnamEntryCommandHandler> _logger;

    public CreateCnamEntryCommandHandler(
        ICnamCatalogRepository repository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CreateCnamEntryCommandHandler> logger)
    {
        _repository = repository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<CnamNomenclatureEntryDto>> Handle(CreateCnamEntryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.CodeActe))
            {
                return Result<CnamNomenclatureEntryDto>.Failure("Le code acte est obligatoire.");
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<CnamNomenclatureEntryDto>.Failure(clinicResult.Error ?? "Impossible de résoudre la clinique.");
            }

            // Existence check auto-scopes to the caller's clinic via the query filter → uniqueness is per-clinic (#5).
            if (await _repository.CodeActeExistsAsync(request.CodeActe, null, cancellationToken))
            {
                return Result<CnamNomenclatureEntryDto>.Failure(
                    $"Un acte avec le code « {request.CodeActe.Trim()} » existe déjà.");
            }

            CnamNomenclatureEntry entry;
            try
            {
                entry = new CnamNomenclatureEntry(
                    Guid.NewGuid(),
                    clinicResult.Value,
                    request.CodeActe,
                    request.DesignationFr,
                    request.LettreCle,
                    request.Coefficient,
                    request.Category);
            }
            catch (ArgumentException ex)
            {
                return Result<CnamNomenclatureEntryDto>.Failure(ex.Message);
            }

            await _repository.AddAsync(entry, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created CNAM catalog entry {Id} ({Code})", entry.Id, entry.CodeActe);
            return Result<CnamNomenclatureEntryDto>.Success(CnamEntryMapper.ToDto(entry));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating CNAM catalog entry");
            return Result<CnamNomenclatureEntryDto>.Failure("Erreur lors de la création de l'acte.");
        }
    }
}
