using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Import;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

/// <summary>
/// Commits a patient CSV import (L5) — « a dentist arriving with 3 000 patients in a spreadsheet types them in by
/// hand », which the spec names as the single thing that stops most switchers.
///
/// <para><b>Why this is a command while the preview is a query.</b> A command's namespace is what makes
/// <c>RealtimeBroadcastBehavior</c> emit the <c>patients</c> key, so an import fires <b>one</b> broadcast for the
/// whole file. That is also the reason it does not simply <c>Send</c> a <see cref="CreatePatientCommand"/> per row:
/// 3 000 commands would be 3 000 broadcasts and 3 000 refetches in every open browser in the practice. The rules
/// are shared instead of the pipeline — see <see cref="PatientFromRequest"/>.</para>
///
/// <para><b>One save per row, deliberately.</b> The spec requires an import to be « all-or-nothing per <b>row</b>,
/// never a silent partial commit ». A single save for the file cannot give that: one refused row would take the
/// other 2 999 with it. Per-row saves make each row atomic on its own and let the report state, for every line,
/// exactly what happened — which is what makes a partial import <i>reported</i> rather than silent. Each committed
/// row is then detached (<see cref="IUnitOfWork.StopTracking"/>) so the change tracker does not grow to 3 000
/// entries that EF re-scans on every subsequent save.</para>
/// </summary>
public class ImportPatientsCommand : IRequest<Result<PatientImportResultDto>>
{
    public byte[] FileContent { get; set; } = Array.Empty<byte>();

    /// <summary>The mapping the operator confirmed on the preview. Same shape and same tokens.</summary>
    public Dictionary<string, int>? Mapping { get; set; }

    /// <summary>
    /// File lines the operator explicitly chose to create <b>despite</b> a duplicate match — the spec's « offer skip
    /// / create-anyway per row, defaulting to skip ».
    ///
    /// <para>⚠️ The default is the <b>empty</b> list, i.e. skip every duplicate. That direction is not a
    /// convenience: <c>Patient</c> has no merge and no soft delete, so a duplicate created by mistake is a permanent
    /// second file for one person, while a duplicate wrongly skipped is fixed by importing that one row again.</para>
    /// </summary>
    public List<int> CreateAnywayLines { get; set; } = new();
}

public class ImportPatientsCommandHandler : IRequestHandler<ImportPatientsCommand, Result<PatientImportResultDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ImportPatientsCommandHandler> _logger;

    public ImportPatientsCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<ImportPatientsCommandHandler> logger)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<PatientImportResultDto>> Handle(
        ImportPatientsCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinicId = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicId.IsFailure)
            {
                return Result<PatientImportResultDto>.FailureFrom(clinicId);
            }

            var existing = await _patientRepository.GetIdentitiesAsync(clinicId.Value, cancellationToken);

            // The identical read the preview ran, so « 2 947 à créer » on screen is the number that gets created.
            var plan = PatientImportPlanner.Build(request.FileContent, request.Mapping, existing);
            if (plan.IsFailure)
            {
                return Result<PatientImportResultDto>.FailureFrom(plan);
            }

            var createAnyway = request.CreateAnywayLines.ToHashSet();
            var result = new PatientImportResultDto();

            foreach (var planned in plan.Value!.Rows)
            {
                var dto = PatientImportMapping.ToRowDto(planned);

                if (planned.IsInvalid)
                {
                    dto.Outcome = PatientImportRowOutcome.Invalid;
                    result.FailedCount++;
                    result.Rows.Add(dto);
                    continue;
                }

                if (planned.IsDuplicate && !createAnyway.Contains(planned.Row.LineNumber))
                {
                    dto.Outcome = PatientImportRowOutcome.Skipped;
                    result.SkippedCount++;
                    result.Rows.Add(dto);
                    continue;
                }

                // The same construction and validation a hand-typed patient goes through. A failure here is one the
                // row reader could not see (a value object refusing something it does not surface as a rule), so it
                // becomes this row's error rather than the request's.
                var built = PatientFromRequest.Build(planned.Read.Command!, clinicId.Value);
                if (built.IsFailure)
                {
                    dto.Outcome = PatientImportRowOutcome.Failed;
                    dto.Errors.Add(built.Error ?? "Création refusée.");
                    result.FailedCount++;
                    result.Rows.Add(dto);
                    continue;
                }

                var patient = built.Value!;

                try
                {
                    await _patientRepository.AddAsync(patient, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    // Only after the commit. Detaching an `Added` entry before it would discard the insert with no
                    // exception — a report saying « créé » about a patient who does not exist.
                    _unitOfWork.StopTracking(patient);

                    dto.Outcome = PatientImportRowOutcome.Created;
                    result.CreatedCount++;
                }
                catch (Exception ex) when (ex is not ConflictException and not OperationCanceledException)
                {
                    // One row failing must not abandon the rest of the file — the operator would have no way to know
                    // which rows landed. It is logged (this is the branch that means something unexpected about the
                    // database) and reported on its own line.
                    _logger.LogError(
                        ex,
                        "Patient import: line {Line} failed to save for clinic {ClinicId}",
                        planned.Row.LineNumber,
                        clinicId.Value);

                    // The tracked, uncommitted entity would otherwise be retried — and probably fail again — on the
                    // NEXT row's save, which would then be reported as that row failing.
                    _unitOfWork.StopTracking(patient);

                    dto.Outcome = PatientImportRowOutcome.Failed;
                    dto.Errors.Add("Enregistrement refusé par la base de données.");
                    result.FailedCount++;
                }

                result.Rows.Add(dto);
            }

            return Result<PatientImportResultDto>.Success(result);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientImportResultDto>.Failure($"Échec de l'import : {ex.Message}");
        }
    }
}
