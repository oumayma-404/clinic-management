using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Medications.Commands;

// Create a global medication catalog entry. AdminOnly (controller-enforced). New entries seed the
// provisional "à vérifier" flag. A duplicate brand + strength + form is rejected with a French message.
public class CreateMedicationCommand : IRequest<Result<MedicationDto>>
{
    public string BrandName { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    public string Strength { get; set; } = string.Empty;
    public List<string> Dcis { get; set; } = new();
}

public class CreateMedicationCommandHandler : IRequestHandler<CreateMedicationCommand, Result<MedicationDto>>
{
    private readonly IMedicationCatalogRepository _repository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateMedicationCommandHandler> _logger;

    public CreateMedicationCommandHandler(
        IMedicationCatalogRepository repository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CreateMedicationCommandHandler> logger)
    {
        _repository = repository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<MedicationDto>> Handle(CreateMedicationCommand request, CancellationToken cancellationToken)
    {
        try
        {
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

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<MedicationDto>.Failure(clinicResult.Error ?? "Impossible de résoudre le cabinet.");
            }

            // Existence check auto-scopes to the caller's clinic via the query filter → uniqueness is per-clinic (#5).
            if (await _repository.BrandExistsAsync(request.BrandName, request.Form, request.Strength, null, cancellationToken))
            {
                return Result<MedicationDto>.Failure(
                    $"Le médicament « {request.BrandName.Trim()} » (même forme et dosage) existe déjà.");
            }

            Medication medication;
            try
            {
                medication = new Medication(
                    Guid.NewGuid(),
                    clinicResult.Value,
                    request.BrandName,
                    request.Form,
                    request.Strength,
                    dcis);
            }
            catch (ArgumentException ex)
            {
                return Result<MedicationDto>.Failure(ex.Message);
            }

            await _repository.AddAsync(medication, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created medication catalog entry {Id} ({Brand})", medication.Id, medication.BrandName);
            return Result<MedicationDto>.Success(MedicationMapper.ToDto(medication));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error creating medication catalog entry");
            return Result<MedicationDto>.Failure("Erreur lors de la création du médicament.");
        }
    }
}
