using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Notifications.Commands;

/// <summary>
/// Marks a single notification read for the current user. Idempotent. A notification from another clinic
/// reads as "not found" (tenant-isolation convention).
/// </summary>
public class MarkNotificationReadCommand : IRequest<Result>
{
    public Guid Id { get; set; }
}

public class MarkNotificationReadCommandHandler : IRequestHandler<MarkNotificationReadCommand, Result>
{
    private readonly IStaffNotificationRepository _notifications;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<MarkNotificationReadCommandHandler> _logger;

    public MarkNotificationReadCommandHandler(
        IStaffNotificationRepository notifications,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        ILogger<MarkNotificationReadCommandHandler> logger)
    {
        _notifications = notifications;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result.Failure("Utilisateur introuvable.");
            }

            var notification = await _notifications.GetByIdAsync(request.Id, cancellationToken);
            // Cross-clinic (or missing) reads as not-found — never confirm another clinic's id exists.
            if (notification == null || notification.ClinicId != user.ClinicId)
            {
                return Result.Failure("Notification introuvable.");
            }

            // Idempotent: only insert a marker if one doesn't already exist.
            if (!await _notifications.ReadMarkerExistsAsync(request.Id, userId, cancellationToken))
            {
                await _notifications.AddReadMarkerAsync(new NotificationRead(request.Id, userId), cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // AC-13.2: the detail goes to the log; the caller only ever sees French guidance.
            _logger.LogError(ex, "Unhandled failure marking notification read");
            return Result.Failure("Erreur lors de la mise à jour de la notification.");
        }
    }
}
