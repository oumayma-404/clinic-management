using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Patients.Commands;

/// <summary>
/// Archive a patient: hidden from lists, search, recall and every picker, nothing destroyed, fully reversible.
///
/// This is the escape hatch that keeps the delete button meaningful. Deletion is refused whenever any clinical
/// or financial record is attached, and this app has no merge and no soft delete — so without archiving a
/// duplicate patient with a single booking could never be removed from the list.
/// </summary>
public class ArchivePatientCommand : IRequest<Result<PatientDto>>
{
    public Guid Id { get; set; }
    public string? Reason { get; set; }
}

public class ArchivePatientCommandHandler : IRequestHandler<ArchivePatientCommand, Result<PatientDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public ArchivePatientCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PatientDto>> Handle(ArchivePatientCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PatientDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            // Tenant isolation: a patient from another clinic reads as "not found".
            var patient = await _patientRepository.GetByIdAsync(request.Id, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<PatientDto>.Failure("Patient introuvable.");
            }

            // Archiving must not hide money owed or a booked visit — that would be a way to make a real
            // obligation quietly disappear from « Créances » and the calendar.
            var blockers = await _patientRepository.GetArchiveBlockersAsync(
                patient.Id, DateTime.UtcNow, cancellationToken);
            if (blockers.Any)
            {
                return Result<PatientDto>.Failure(PatientArchiveRules.DescribeBlockers(blockers)!);
            }

            patient.Archive(request.Reason);

            await _patientRepository.UpdateAsync(patient, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<PatientDto>.Success(patient.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientDto>.Failure($"Erreur lors de l'archivage du patient : {ex.Message}");
        }
    }
}
