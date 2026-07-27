using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Patients.Queries;

/// <summary>
/// What blocks this patient's deletion, and whether archiving is available instead.
/// Read by the confirm dialog when it opens so the user learns the answer before clicking, not after.
/// </summary>
public class GetPatientDeletionCheckQuery : IRequest<Result<PatientDeletionCheckDto>>
{
    public Guid PatientId { get; set; }
}

public class GetPatientDeletionCheckQueryHandler
    : IRequestHandler<GetPatientDeletionCheckQuery, Result<PatientDeletionCheckDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetPatientDeletionCheckQueryHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<PatientDeletionCheckDto>> Handle(
        GetPatientDeletionCheckQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PatientDeletionCheckDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<PatientDeletionCheckDto>.Failure("Patient introuvable.");
            }

            var counts = await _patientRepository.GetLinkedDataCountsAsync(patient.Id, cancellationToken);
            var archiveBlockers = await _patientRepository.GetArchiveBlockersAsync(
                patient.Id, DateTime.UtcNow, cancellationToken);

            return Result<PatientDeletionCheckDto>.Success(new PatientDeletionCheckDto
            {
                PatientId = patient.Id,
                PatientName = patient.GetFullName(),
                CanDelete = !counts.Any,
                IsArchived = patient.IsArchived,
                CanArchive = !patient.IsArchived && !archiveBlockers.Any,
                ArchiveBlockedReason = patient.IsArchived
                    ? null
                    : PatientArchiveRules.DescribeBlockers(archiveBlockers),
                Blockers = PatientDeletionBlockers.From(counts)
                    .Select(b => new PatientDeletionBlockerDto
                    {
                        Kind = b.Kind,
                        Label = b.Label,
                        Count = b.Count,
                        Tab = b.Tab
                    })
                    .ToList()
            });
        }
        catch (Exception ex)
        {
            return Result<PatientDeletionCheckDto>.Failure(
                $"Erreur lors de la vérification du patient : {ex.Message}");
        }
    }
}
