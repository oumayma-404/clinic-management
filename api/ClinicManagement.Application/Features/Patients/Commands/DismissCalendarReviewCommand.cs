using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

/// <summary>
/// « Ne plus afficher » — take one or many records off « Patients à compléter » without saying they are correct,
/// or put them back.
///
/// <para>⚠️ <b>This is emphatically NOT <see cref="ConfirmCalendarImportCommand"/>, and the distinction is
/// load-bearing.</b> That command clears <c>Patient.CalendarImportPendingReviewSince</c>, which is what marks a
/// record as conjured-from-a-calendar-title-and-unconfirmed — and that stamp is the signal « Annuler cet import »
/// uses to find what a run created. A « ne plus afficher » implemented by clearing it would be indistinguishable
/// from a human confirmation and would silently destroy the evidence the undo depends on: the same self-inflicted
/// loss as cancelling a visit, which nulls <c>Appointment.GoogleCalendarEventId</c> and makes the row
/// unidentifiable afterwards.</para>
///
/// <para>« Je ne veux plus voir cette ligne » and « j'ai vérifié que cette fiche est correcte » are different
/// facts about a record. A product that stores them in one column can never tell them apart again — so this writes
/// its own, and the per-row « C'est correct » on the patient's fiche stays exactly what it was.</para>
///
/// <para><b>One command with a <c>bool</c> and a list, three routes</b> —
/// <c>DisregardVisitsCommand</c>'s shape, one tab over, and for its reasons.</para>
/// </summary>
public class DismissCalendarReviewCommand : IRequest<Result<DismissCalendarReviewResultDto>>
{
    public List<Guid> PatientIds { get; set; } = new();

    /// <summary>True to take the records off the list, false to put them back.</summary>
    public bool Dismiss { get; set; }
}

/// <summary>What the call did, in the terms the screen reports back.</summary>
public class DismissCalendarReviewResultDto
{
    public int Changed { get; set; }

    /// <summary>
    /// Ids the call did not touch — unknown, another clinic's, already in the requested state, or no longer
    /// awaiting review at all. Reported rather than refused, for <c>DisregardVisitsCommand</c>'s reason.
    /// </summary>
    public List<Guid> Skipped { get; set; } = new();
}

public class DismissCalendarReviewCommandHandler
    : IRequestHandler<DismissCalendarReviewCommand, Result<DismissCalendarReviewResultDto>>
{
    /// <inheritdoc cref="Appointments.Commands.DisregardVisitsCommandHandler.MaxIds"/>
    public const int MaxIds = 500;

    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<DismissCalendarReviewCommandHandler> _logger;

    public DismissCalendarReviewCommandHandler(
        IPatientRepository patientRepository,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        ILogger<DismissCalendarReviewCommandHandler> logger)
    {
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<DismissCalendarReviewResultDto>> Handle(
        DismissCalendarReviewCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var ids = request.PatientIds.Distinct().ToList();

            if (ids.Count == 0)
            {
                return Result<DismissCalendarReviewResultDto>.Failure("Aucun patient sélectionné.");
            }

            if (ids.Count > MaxIds)
            {
                return Result<DismissCalendarReviewResultDto>.Failure(
                    $"Vous ne pouvez traiter que {MaxIds} patients à la fois.");
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<DismissCalendarReviewResultDto>.Failure(clinicResult.Error ?? ErrorMessages.Generic);
            }

            var nowUtc = DateTime.UtcNow;
            var result = new DismissCalendarReviewResultDto();

            foreach (var id in ids)
            {
                var patient = await _patientRepository.GetByIdAsync(id, cancellationToken);

                if (patient is null || patient.ClinicId != clinicResult.Value)
                {
                    result.Skipped.Add(id);
                    continue;
                }

                // A record nobody is asking about cannot be taken off the list it is not on. Restoring is not
                // gated the same way: a record whose stamp was cleared while it was dismissed has already had its
                // dismissal cleared with it (see `Patient.UpdatePersonalInfo`), so there is nothing to restore.
                if (request.Dismiss && patient.CalendarImportPendingReviewSince is null)
                {
                    result.Skipped.Add(id);
                    continue;
                }

                var before = patient.IsCalendarReviewDismissed;

                if (request.Dismiss)
                {
                    patient.DismissCalendarReview(nowUtc);
                }
                else
                {
                    patient.RestoreCalendarReview();
                }

                if (patient.IsCalendarReviewDismissed == before)
                {
                    result.Skipped.Add(id);
                    continue;
                }

                await _patientRepository.UpdateAsync(patient, cancellationToken);
                result.Changed++;
            }

            if (result.Changed > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<DismissCalendarReviewResultDto>.Success(result);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(
                ex, "Failed to set the calendar-review dismissal on {Count} patient(s)", request.PatientIds.Count);
            return Result<DismissCalendarReviewResultDto>.Failure(ErrorMessages.Generic);
        }
    }
}
