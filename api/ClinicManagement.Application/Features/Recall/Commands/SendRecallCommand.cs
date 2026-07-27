using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Recall.Commands;

/// <summary>
/// Optionally send a recall (« relance ») to a patient over the configured SMS/WhatsApp channel(s) (AC-1.3),
/// then record the contact + snooze so they leave the active list. The actual send is connectivity-gated and
/// done later by the dispatcher; the enqueue is best-effort (a channel not being configured is not an error).
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

            // Enqueue the outbound recall (best-effort; per-channel, connectivity-gated at send).
            await _reminderScheduler.ScheduleRecallAsync(
                clinic.Value, patient.Id, patient.GetFullName(), reason, cancellationToken);

            // Sending a recall counts as contacting the patient — stamp it and snooze so it leaves the list.
            patient.MarkRecallContacted(DateTime.UtcNow.AddDays(ContactedSnoozeDays), reason);
            await _patientRepository.UpdateAsync(patient, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Erreur lors de l'envoi de la relance : {ex.Message}");
        }
    }
}
