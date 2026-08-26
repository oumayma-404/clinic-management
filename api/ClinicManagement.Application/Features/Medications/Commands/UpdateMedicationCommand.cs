using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Medications.Commands;

// Update a global medication catalog entry. AdminOnly. Changing brand + strength + form to one already held
// by another entry is rejected (duplicate). Unknown id → not-found failure. Replaces the full DCI set.
public class UpdateMedicationCommand : IRequest<Result<MedicationDto>>
{
    public Guid Id { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    public string Strength { get; set; } = string.Empty;
    public List<string> Dcis { get; set; } = new();

    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the user was
    /// editing; <c>0</c> means « not supplied » and skips the check (see <c>IUnitOfWork.SetExpectedVersion</c>).
    /// </summary>
    public uint Version { get; set; }
}

public class UpdateMedicationCommandHandler : IRequestHandler<UpdateMedicationCommand, Result<MedicationDto>>
{
    private readonly IMedicationCatalogRepository _repository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateMedicationCommandHandler> _logger;

    public UpdateMedicationCommandHandler(
        IMedicationCatalogRepository repository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<UpdateMedicationCommandHandler> logger)
    {
        _repository = repository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<MedicationDto>> Handle(UpdateMedicationCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Authoritative tenant guard: resolve the caller's clinic from the DB rather than relying on the
            // EF global query filter, which is FAIL-OPEN — a token minted without a clinic_id claim leaves it
            // inactive, and this row could then be reached by id from another clinic (audit § 2, finding 10).
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<MedicationDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var medication = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (medication is null || medication.ClinicId != clinicResult.Value)
            {
                return Result<MedicationDto>.Failure("Médicament introuvable.");
            }

            if (string.IsNullOrWhiteSpace(request.BrandName))
            {
                return Result<MedicationDto>.Failure("Le nom commercial est obligatoire.");
            }

            var dcis = (request.Dcis ?? new List<string>())
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .ToList();
            if (dcis.Count == 0)
            {
                return Result<MedicationDto>.Failure("Au moins une DCI (molécule) est requise.");
            }

            if (await _repository.BrandExistsAsync(request.BrandName, request.Form, request.Strength, request.Id, cancellationToken))
            {
                return Result<MedicationDto>.Failure(
                    $"Le médicament « {request.BrandName.Trim()} » (même forme et dosage) existe déjà.");
            }

            try
            {
                medication.Update(request.BrandName, request.Form, request.Strength);
                medication.ReplaceActiveIngredients(dcis);
            }
            catch (ArgumentException ex)
            {
                return Result<MedicationDto>.Failure(ex.Message);
            }

            // Band B — validated against the copy the USER was editing, not the row this handler just read.
            _unitOfWork.SetExpectedVersion(medication, request.Version);

            await _repository.UpdateAsync(medication, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated medication catalog entry {Id}", medication.Id);
            return Result<MedicationDto>.Success(MedicationMapper.ToDto(medication));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error updating medication catalog entry {Id}", request.Id);
            return Result<MedicationDto>.Failure("Erreur lors de la mise à jour du médicament.");
        }
    }
}
