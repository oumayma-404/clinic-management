using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Commands;

/// <summary>
/// Cancels part or all of a recurring series (AC-2.2): a single occurrence, this occurrence and all following
/// ones, or the whole series. Completed/already-cancelled occurrences are left untouched. "Following" and
/// "WholeSeries" also deactivate the series template. Returns the number of appointments cancelled.
/// </summary>
public class CancelRecurringSeriesCommand : IRequest<Result<int>>
{
    public Guid RecurringAppointmentId { get; set; }
    public string Scope { get; set; } = string.Empty; // Occurrence / Following / WholeSeries
    /// <summary>Required for Occurrence/Following — the occurrence the scope is anchored on.</summary>
    public Guid? FromAppointmentId { get; set; }
    public string? Reason { get; set; }
}

public class CancelRecurringSeriesCommandHandler : IRequestHandler<CancelRecurringSeriesCommand, Result<int>>
{
    private readonly IRecurringAppointmentRepository _recurringRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReminderScheduler _reminderScheduler;

    public CancelRecurringSeriesCommandHandler(
        IRecurringAppointmentRepository recurringRepository,
        IAppointmentRepository appointmentRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        IReminderScheduler reminderScheduler)
    {
        _recurringRepository = recurringRepository;
        _appointmentRepository = appointmentRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _reminderScheduler = reminderScheduler;
    }

    public async Task<Result<int>> Handle(CancelRecurringSeriesCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (!Enum.TryParse<RecurringSeriesScope>(request.Scope, ignoreCase: true, out var scope))
                return Result<int>.Failure("Portée invalide (Occurrence/Following/WholeSeries).");

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<int>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var series = await _recurringRepository.GetByIdAsync(request.RecurringAppointmentId, cancellationToken);
            if (series == null || series.ClinicId != clinic.Value)
                return Result<int>.Failure("Série récurrente introuvable.");

            var seriesAppointments = (await _appointmentRepository.GetByClinicIdAsync(clinic.Value, cancellationToken: cancellationToken))
                .Where(a => a.RecurringAppointmentId == series.Id)
                .ToList();

            var toCancel = new List<Appointment>();

            switch (scope)
            {
                case RecurringSeriesScope.Occurrence:
                    if (!request.FromAppointmentId.HasValue)
                        return Result<int>.Failure("L'occurrence à annuler est requise.");
                    var single = seriesAppointments.FirstOrDefault(a => a.Id == request.FromAppointmentId.Value);
                    if (single == null)
                        return Result<int>.Failure("Occurrence introuvable dans la série.");
                    toCancel.Add(single);
                    break;

                case RecurringSeriesScope.Following:
                    if (!request.FromAppointmentId.HasValue)
                        return Result<int>.Failure("L'occurrence de départ est requise.");
                    var anchor = seriesAppointments.FirstOrDefault(a => a.Id == request.FromAppointmentId.Value);
                    if (anchor == null)
                        return Result<int>.Failure("Occurrence introuvable dans la série.");
                    toCancel.AddRange(seriesAppointments.Where(a => a.AppointmentDateTime >= anchor.AppointmentDateTime));
                    series.Deactivate();
                    await _recurringRepository.UpdateAsync(series, cancellationToken);
                    break;

                case RecurringSeriesScope.WholeSeries:
                    toCancel.AddRange(seriesAppointments);
                    series.Deactivate();
                    await _recurringRepository.UpdateAsync(series, cancellationToken);
                    break;
            }

            var cancelled = 0;
            var cancelledIds = new List<Guid>();
            foreach (var appointment in toCancel)
            {
                if (appointment.Status is AppointmentStatus.Completed or AppointmentStatus.Cancelled)
                    continue;

                appointment.Cancel(request.Reason);
                await _appointmentRepository.UpdateAsync(appointment, cancellationToken);
                cancelledIds.Add(appointment.Id);
                cancelled++;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // L3b — void the queued reminders, post-commit and best-effort, exactly as the single-appointment
            // cancel does. This file referenced no reminder type at all, so cancelling a 12-visit series left
            // twelve reminders to fail one by one over the following months, filling « Échecs (7 j) » with
            // failures that were the *correct* outcome — which is how a dentist learns to ignore the counter
            // that warns about the real ones. The dispatcher's own status re-check would drop each row, but only
            // by recording it as a failure; suppressing it here is what makes it a non-event.
            foreach (var appointmentId in cancelledIds)
            {
                await _reminderScheduler.VoidForAppointmentAsync(appointmentId, cancellationToken);
            }

            return Result<int>.Success(cancelled);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // A-7 (second copy).
            return Result<int>.Failure("Erreur lors de l'annulation de la série. Veuillez réessayer.");
        }
    }
}
