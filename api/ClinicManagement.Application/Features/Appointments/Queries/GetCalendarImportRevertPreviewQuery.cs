using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Queries;

/// <summary>
/// What « Annuler cet import » would delete, and what it would keep — asked before anything is written.
///
/// <para><b>The dry run IS the safety here.</b> The person pressing the button is the cabinet, not the vendor:
/// nobody is holding a backup, nobody is watching the row counts, and the click removes patient records. So the
/// screen shows the list rather than a number, and every row that will survive names its own reason in French.</para>
///
/// <para>It reads the same <c>CalendarImportRunContents</c> and applies the same
/// <c>CalendarImportRunPresentation.PartitionVisits</c> as the undo itself, so the preview cannot promise
/// something the command then does differently.</para>
/// </summary>
public class GetCalendarImportRevertPreviewQuery : IRequest<Result<CalendarImportRevertPreviewDto>>
{
    public Guid RunId { get; set; }
}

public class GetCalendarImportRevertPreviewQueryHandler
    : IRequestHandler<GetCalendarImportRevertPreviewQuery, Result<CalendarImportRevertPreviewDto>>
{
    private readonly ICalendarImportRunRepository _runRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetCalendarImportRevertPreviewQueryHandler> _logger;

    public GetCalendarImportRevertPreviewQueryHandler(
        ICalendarImportRunRepository runRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetCalendarImportRevertPreviewQueryHandler> logger)
    {
        _runRepository = runRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<CalendarImportRevertPreviewDto>> Handle(
        GetCalendarImportRevertPreviewQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<CalendarImportRevertPreviewDto>.Failure(clinicResult.Error ?? ErrorMessages.Generic);
            }

            var clinicId = clinicResult.Value;
            var run = await _runRepository.GetByIdAsync(request.RunId, cancellationToken);

            if (run is null || run.ClinicId != clinicId)
            {
                return Result<CalendarImportRevertPreviewDto>.Failure("Import introuvable.");
            }

            var contents = await _runRepository.GetContentsAsync(clinicId, run.Id, cancellationToken);
            var (deletableAppointmentIds, kept) =
                CalendarImportRunPresentation.PartitionVisits(contents.Visits);

            var patientsToDelete = 0;

            var goingByPatient = contents.Visits
                .Where(v => v.PatientId.HasValue && deletableAppointmentIds.Contains(v.AppointmentId))
                .GroupBy(v => v.PatientId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var patient in contents.Patients)
            {
                var counts = await _patientRepository.GetLinkedDataCountsAsync(
                    patient.PatientId, cancellationToken);

                goingByPatient.TryGetValue(patient.PatientId, out var going);

                var otherwiseAttached = counts.Total - counts.Appointments - counts.Notifications;
                var appointmentsStaying = counts.Appointments - going;

                if (otherwiseAttached <= 0 && appointmentsStaying <= 0)
                {
                    patientsToDelete++;
                    continue;
                }

                var remaining = counts with
                {
                    Appointments = Math.Max(0, appointmentsStaying),
                    Notifications = 0
                };

                kept.Add(new CalendarImportKeptRowDto
                {
                    Id = patient.PatientId,
                    Label = patient.FullName,
                    When = null,
                    Reason = PatientDeletionBlockers.DescribeAttached(remaining)
                });
            }

            return Result<CalendarImportRevertPreviewDto>.Success(new CalendarImportRevertPreviewDto
            {
                RunId = run.Id,
                StartedAtUtc = run.StartedAtUtc,
                AlreadyReverted = run.IsReverted,
                AppointmentsToDelete = deletableAppointmentIds.Count,
                PatientsToDelete = patientsToDelete,
                Kept = kept
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Failed to preview the revert of calendar import run {RunId}", request.RunId);
            return Result<CalendarImportRevertPreviewDto>.Failure(ErrorMessages.Generic);
        }
    }
}
