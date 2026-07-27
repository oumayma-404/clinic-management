using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.CnamNomenclature.Commands;

// Update a global CNAM catalog entry (FR-5.1). AdminOnly. Changing the code acte to one already held by
// another entry is rejected (duplicate). Unknown id → not-found failure.
public class UpdateCnamEntryCommand : IRequest<Result<CnamNomenclatureEntryDto>>
{
    public Guid Id { get; set; }
    public string CodeActe { get; set; } = string.Empty;
    public string DesignationFr { get; set; } = string.Empty;
    public string LettreCle { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public string Category { get; set; } = string.Empty;
}

public class UpdateCnamEntryCommandHandler : IRequestHandler<UpdateCnamEntryCommand, Result<CnamNomenclatureEntryDto>>
{
    private readonly ICnamCatalogRepository _repository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateCnamEntryCommandHandler> _logger;

    public UpdateCnamEntryCommandHandler(
        ICnamCatalogRepository repository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<UpdateCnamEntryCommandHandler> logger)
    {
        _repository = repository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<CnamNomenclatureEntryDto>> Handle(UpdateCnamEntryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Authoritative tenant guard: resolve the caller's clinic from the DB rather than relying on the
            // EF global query filter, which is FAIL-OPEN — a token minted without a clinic_id claim leaves it
            // inactive, and this row could then be reached by id from another clinic (audit § 2, finding 10).
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<CnamNomenclatureEntryDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var entry = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (entry is null || entry.ClinicId != clinicResult.Value)
            {
                return Result<CnamNomenclatureEntryDto>.Failure("Acte introuvable.");
            }

            if (string.IsNullOrWhiteSpace(request.CodeActe))
            {
                return Result<CnamNomenclatureEntryDto>.Failure("Le code acte est obligatoire.");
            }

            if (await _repository.CodeActeExistsAsync(request.CodeActe, request.Id, cancellationToken))
            {
                return Result<CnamNomenclatureEntryDto>.Failure(
                    $"Un acte avec le code « {request.CodeActe.Trim()} » existe déjà.");
            }

            try
            {
                entry.Update(request.CodeActe, request.DesignationFr, request.LettreCle, request.Coefficient, request.Category);
            }
            catch (ArgumentException ex)
            {
                return Result<CnamNomenclatureEntryDto>.Failure(ex.Message);
            }

            await _repository.UpdateAsync(entry, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated CNAM catalog entry {Id}", entry.Id);
            return Result<CnamNomenclatureEntryDto>.Success(CnamEntryMapper.ToDto(entry));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error updating CNAM catalog entry {Id}", request.Id);
            return Result<CnamNomenclatureEntryDto>.Failure("Erreur lors de la mise à jour de l'acte.");
        }
    }
}
