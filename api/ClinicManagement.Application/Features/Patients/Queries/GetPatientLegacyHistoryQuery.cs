using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Patients.Queries;

/// <summary>
/// « Ancien logiciel » — the patient's lines from the cabinet's previous software. Not paged: bounded by one
/// patient's old record (tens of lines), and the tab shows them all.
/// </summary>
public class GetPatientLegacyHistoryQuery : IRequest<Result<List<PatientLegacyHistoryEntryDto>>>
{
    public Guid PatientId { get; set; }
}

public class GetPatientLegacyHistoryQueryHandler
    : IRequestHandler<GetPatientLegacyHistoryQuery, Result<List<PatientLegacyHistoryEntryDto>>>
{
    private readonly IPatientRepository _patients;
    private readonly IPatientLegacyHistoryRepository _history;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetPatientLegacyHistoryQueryHandler(
        IPatientRepository patients, IPatientLegacyHistoryRepository history, ICurrentClinicResolver clinicResolver)
    {
        _patients = patients;
        _history = history;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<List<PatientLegacyHistoryEntryDto>>> Handle(
        GetPatientLegacyHistoryQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<List<PatientLegacyHistoryEntryDto>>.Failure(clinic.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patients.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinic.Value)
            {
                return Result<List<PatientLegacyHistoryEntryDto>>.Failure("Patient introuvable.");
            }

            var entries = await _history.GetByPatientAsync(request.PatientId, cancellationToken);
            return Result<List<PatientLegacyHistoryEntryDto>>.Success(entries.Select(PatientLegacyHistoryEntryDto.From).ToList());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<List<PatientLegacyHistoryEntryDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
