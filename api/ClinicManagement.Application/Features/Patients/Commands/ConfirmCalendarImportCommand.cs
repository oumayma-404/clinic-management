using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Patients.Commands;

/// <summary>
/// Confirms a calendar-imported fiche as correct with nothing to change (<c>calendar-import-review</c> AC-8).
///
/// <para>Its own command rather than a flag on the update: without it, a fiche whose name is simply right could
/// only be cleared by editing something, which teaches people to make a pointless edit to silence a prompt.
/// Every other route out of the review state is <c>Patient.UpdatePersonalInfo</c>, which clears it itself.</para>
/// </summary>
public class ConfirmCalendarImportCommand : IRequest<Result<PatientDto>>
{
    public Guid Id { get; set; }
}

public class ConfirmCalendarImportCommandHandler : IRequestHandler<ConfirmCalendarImportCommand, Result<PatientDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmCalendarImportCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PatientDto>> Handle(ConfirmCalendarImportCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PatientDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var patient = await _patientRepository.GetByIdAsync(request.Id, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<PatientDto>.Failure("Patient introuvable.");
            }

            // Idempotent: confirming an already-confirmed fiche is a no-op rather than a refusal, because two
            // people clearing the same notification is the ordinary case.
            patient.ConfirmCalendarImport();

            await _patientRepository.UpdateAsync(patient, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<PatientDto>.Success(patient.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
