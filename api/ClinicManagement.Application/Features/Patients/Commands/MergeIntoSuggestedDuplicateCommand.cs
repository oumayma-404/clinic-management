using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

/// <summary>
/// « Oui, c'est le même patient. » — the answer to the calendar import's duplicate question
/// (<c>calendar-import-duplicate-merge</c> AC-9 to AC-13).
///
/// <para><b>This is the product's first patient merge</b>, and it is deliberately not a general one.
/// <see cref="ArchivePatientCommand"/> and <see cref="CreatePatientCommand"/> both state that this app has no
/// merge and no soft delete, and that is still true of any two patients a user might pick: what this command
/// merges is a <b>placeholder the calendar import created</b>, into the <b>one candidate the import itself
/// stamped</b>, and only while nothing real has been attached to it. Widening any of those three is a different
/// feature with different teeth.</para>
///
/// <para>⚠️ It cannot reuse <see cref="DeletePatientCommand"/>, whose refusal is « anything at all is attached »:
/// an imported placeholder always has at least the appointment it was created for, which is precisely the thing
/// this command moves. So the blocker test here is that refusal <b>minus appointments</b> — and it is taken
/// <b>before anything is written</b>, because a refusal that has already reassigned the séances is not a
/// refusal.</para>
/// </summary>
public class MergeIntoSuggestedDuplicateCommand : IRequest<Result<MergeSuggestedDuplicateResult>>
{
    /// <summary>The imported placeholder — the record that goes away.</summary>
    public Guid Id { get; set; }
}

/// <summary>What the merge did, in the terms the screen reports back.</summary>
public class MergeSuggestedDuplicateResult
{
    public Guid SurvivingPatientId { get; set; }
    public string SurvivingPatientName { get; set; } = string.Empty;
    public int AppointmentsMoved { get; set; }

    /// <summary>
    /// True when the surviving patient now holds two appointments whose times overlap. <b>Reported, not
    /// refused</b>: the booking constraint is keyed on the practitioner and an imported appointment names none, so
    /// nothing in the database stops it — and the practice, not this command, is who decides which séance stands.
    /// </summary>
    public bool OverlapsExisting { get; set; }
}

public class MergeIntoSuggestedDuplicateCommandHandler
    : IRequestHandler<MergeIntoSuggestedDuplicateCommand, Result<MergeSuggestedDuplicateResult>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IStaffNotificationRepository _staffNotificationRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<MergeIntoSuggestedDuplicateCommandHandler> _logger;

    public MergeIntoSuggestedDuplicateCommandHandler(
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        IStaffNotificationRepository staffNotificationRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<MergeIntoSuggestedDuplicateCommandHandler> logger)
    {
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _staffNotificationRepository = staffNotificationRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<MergeSuggestedDuplicateResult>> Handle(
        MergeIntoSuggestedDuplicateCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<MergeSuggestedDuplicateResult>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var clinicId = clinicResult.Value;

            var duplicate = await _patientRepository.GetByIdAsync(request.Id, cancellationToken);
            if (duplicate == null || duplicate.ClinicId != clinicId)
            {
                // Also the answer to a second « Oui » on the same row: the placeholder is already gone.
                throw new NotFoundException("Patient introuvable.");
            }

            if (!duplicate.CalendarImportSuggestedDuplicateId.HasValue)
            {
                return Result<MergeSuggestedDuplicateResult>.Failure(
                    "Aucun patient similaire n'est proposé pour cette fiche.");
            }

            var survivor = await _patientRepository.GetByIdAsync(
                duplicate.CalendarImportSuggestedDuplicateId.Value, cancellationToken);

            // A suggestion whose target is gone is an expired question, not a fault — and never a cross-clinic one.
            if (survivor == null || survivor.ClinicId != clinicId || survivor.Id == duplicate.Id)
            {
                duplicate.RejectCalendarImportSuggestion();
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<MergeSuggestedDuplicateResult>.Failure(
                    "Le patient proposé n'existe plus. La proposition a été retirée.");
            }

            // ⚠️ Taken BEFORE any write, and with `Appointments` zeroed: the appointment is what the merge moves,
            // so counting it would refuse every merge there is. Everything else still blocks — a fiche, a note
            // d'honoraires, un devis, un fichier, un état de dent — because deleting the row under one of those is
            // the destruction this codebase refuses everywhere else. Queued reminders block too and are named as
            // « rappels »: they are addressed to a patient who is about to stop existing.
            var counts = await _patientRepository.GetLinkedDataCountsAsync(duplicate.Id, cancellationToken);
            var blocking = counts with { Appointments = 0 };
            if (blocking.Any)
            {
                return Result<MergeSuggestedDuplicateResult>.Failure(
                    $"Impossible de fusionner {duplicate.GetFullName()} : "
                    + $"{PatientDeletionBlockers.DescribeAttached(blocking)}. "
                    + "Complétez la fiche plutôt que de la fusionner.");
            }

            var moving = (await _appointmentRepository.GetByPatientIdAsync(duplicate.Id, cancellationToken)).ToList();
            var survivorAppointments =
                (await _appointmentRepository.GetByPatientIdAsync(survivor.Id, cancellationToken)).ToList();

            var overlaps = moving.Any(m => survivorAppointments.Any(
                s => m.AppointmentDateTime < s.AppointmentDateTime + s.Duration
                  && s.AppointmentDateTime < m.AppointmentDateTime + m.Duration));

            // One transaction: a half-done merge leaves a séance on a patient that no longer exists.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                foreach (var appointment in moving)
                {
                    appointment.ReassignToPatient(survivor.Id);
                }

                // Withdrawn rather than moved. The feed row says « patient créé depuis Google Agenda, à compléter »
                // and deep-links to a fiche that is about to be deleted — it does not block the delete (the column
                // carries no foreign key and is not in the counts above), it would simply dangle.
                foreach (var notification in
                    await _staffNotificationRepository.GetByPatientAsync(duplicate.Id, cancellationToken))
                {
                    await _staffNotificationRepository.RemoveAsync(notification, cancellationToken);
                }

                await _patientRepository.DeleteAsync(duplicate.Id, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }

            _logger.LogInformation(
                "Merged calendar-imported patient {DuplicateId} into {SurvivorId}; {Count} appointment(s) moved.",
                duplicate.Id, survivor.Id, moving.Count);

            return Result<MergeSuggestedDuplicateResult>.Success(new MergeSuggestedDuplicateResult
            {
                SurvivingPatientId = survivor.Id,
                SurvivingPatientName = survivor.GetFullName(),
                AppointmentsMoved = moving.Count,
                OverlapsExisting = overlaps,
            });
        }
        catch (Exception ex) when (ex is not ConflictException && ex is not NotFoundException)
        {
            _logger.LogError(ex, "Unhandled failure merging a calendar-imported duplicate");
            return Result<MergeSuggestedDuplicateResult>.Failure(
                "Erreur lors de la fusion des fiches. Veuillez réessayer.");
        }
    }
}
