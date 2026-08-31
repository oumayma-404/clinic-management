using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Recall.Commands;

/// <summary>
/// Send a recall (« relance ») to a patient over the configured SMS/WhatsApp channel(s), then record the
/// contact + snooze so they leave the active list. The actual send is connectivity-gated and done later by
/// the dispatcher.
///
/// AC-P3.1–3.4: the enqueue's outcome is now <b>load-bearing</b>. It used to be fire-and-forget — the command
/// stamped « contacté » and snoozed 30 days even when <c>ScheduleRecallAsync</c> had queued nothing because
/// no channel was configured, and the UI toasted « Rappel envoyé à … ». A patient therefore dropped off the
/// relance list for a month with nobody ever contacted and nobody told. Enqueuing is still not the same as
/// sending, which is why the dispatcher undoes the snooze if every channel ultimately fails (AC-P3.5–3.6).
/// </summary>
public class SendRecallCommand : IRequest<Result<bool>>
{
    public Guid PatientId { get; set; }
    public string? Reason { get; set; }
}

public class SendRecallCommandHandler : IRequestHandler<SendRecallCommand, Result<bool>>
{
    private const int ContactedSnoozeDays = 30;

    private readonly IPatientRepository _patientRepository;
    private readonly IReminderScheduler _reminderScheduler;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public SendRecallCommandHandler(
        IPatientRepository patientRepository,
        IReminderScheduler reminderScheduler,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _reminderScheduler = reminderScheduler;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(SendRecallCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<bool>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinic.Value)
                return Result<bool>.Failure("Patient introuvable.");

            // Refuse rather than pretend. This used to enqueue nothing (the number was undeliverable), then
            // stamp "contacted" and snooze 30 days anyway — so a patient with no phone silently dropped off the
            // relance list for a month and nobody was ever told to call them.
            if (patient.PhoneNumber == null || !PhoneNumber.IsDeliverable(patient.PhoneNumber.Value))
            {
                return Result<bool>.Failure(
                    "Ce patient n'a pas de numéro de téléphone valide : la relance ne peut pas être envoyée. "
                    + "Contactez-le autrement, puis utilisez « Marquer comme contacté ».");
            }

            var reason = string.IsNullOrWhiteSpace(request.Reason) ? patient.RecallReason : request.Reason;

            // Enqueue the outbound recall (per-channel, connectivity-gated at send) and act on what it says.
            var outcome = await _reminderScheduler.ScheduleRecallAsync(
                clinic.Value, patient.Id, patient.GetFullName(), reason, cancellationToken);

            // AC-P3.2 — nothing was queued: refuse, and leave the patient exactly as they were. The message
            // names the fix (the reminder settings) and the alternative (« Marquer comme contacté ») so the
            // refusal is a next step rather than a dead end.
            if (outcome != RecallDispatchOutcome.Enqueued)
            {
                return Result<bool>.Failure(FailureMessage(outcome));
            }

            // AC-P3.4 — a channel IS configured: unchanged behaviour. Sending a recall counts as contacting
            // the patient, so stamp it and snooze so they leave the list.
            patient.MarkRecallContacted(DateTime.UtcNow.AddDays(ContactedSnoozeDays), reason);
            await _patientRepository.UpdateAsync(patient, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<bool>.Failure($"Erreur lors de l'envoi de la relance : {ex.Message}");
        }
    }

    // One French message per non-sending outcome. Each names what to do next: the refusal replaces a
    // success toast, so it has to carry the information that toast was hiding.
    private static string FailureMessage(RecallDispatchOutcome outcome) => outcome switch
    {
        RecallDispatchOutcome.NoChannelConfigured =>
            "Aucun canal de rappel (SMS ou WhatsApp) n'est activé : la relance n'a pas été envoyée. "
            + "Activez un canal dans « Paramètres → Rappels », ou utilisez « Marquer comme contacté » "
            + "après avoir appelé le patient.",
        RecallDispatchOutcome.NoDeliverablePhone =>
            "Ce patient n'a pas de numéro de téléphone valide : la relance ne peut pas être envoyée. "
            + "Contactez-le autrement, puis utilisez « Marquer comme contacté ».",
        // AC-5.1/5.4 — the forfait's own sentence, from MessagingRefusals rather than written again here: the code
        // and the wording are one statement, and a second copy is how a reworded message stops matching the outcome
        // it was paired with. It is deliberately NOT the no-channel sentence above, which would tell the practice to
        // configure a channel it has already configured.
        RecallDispatchOutcome.MessagingAllowanceExhausted => MessagingRefusals.RecallExhausted,
        // Deliberately says nothing about the phone number: the number is fine, and « corrigez le numéro » is
        // the one action that must not put this message back in the queue.
        RecallDispatchOutcome.ReminderConsentRefused =>
            "Ce patient a refusé les rappels automatiques : la relance n'a pas été envoyée. "
            + "Contactez-le par téléphone, puis utilisez « Marquer comme contacté ». Son choix se modifie "
            + "dans sa fiche, section « Rappels automatiques »." ,
        _ =>
            "La relance n'a pas pu être mise en file d'envoi. Le patient reste dans la liste des relances ; "
            + "réessayez dans quelques instants.",
    };
}
