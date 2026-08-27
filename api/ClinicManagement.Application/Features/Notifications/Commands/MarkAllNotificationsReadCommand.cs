using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Notifications.Commands;

/// <summary>
/// Marks ALL of the current user's currently-unread notifications read — not just the visible 50 — so
/// the unread badge always drops to 0 afterward.
/// </summary>
public class MarkAllNotificationsReadCommand : IRequest<Result>
{
}

public class MarkAllNotificationsReadCommandHandler : IRequestHandler<MarkAllNotificationsReadCommand, Result>
{
    private readonly IStaffNotificationRepository _notifications;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public MarkAllNotificationsReadCommandHandler(
        IStaffNotificationRepository notifications,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork)
    {
        _notifications = notifications;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MarkAllNotificationsReadCommand request, CancellationToken cancellationToken)
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

            var now = DateTime.UtcNow;
            // Id-only projection: mark-all only needs each id to build a read marker, not the full rows.
            var unreadIds = await _notifications.GetUnreadIdsForUserAsync(user.ClinicId, userId, user.CreatedAt, now, cancellationToken);

            if (unreadIds.Count == 0)
            {
                return Result.Success();
            }

            foreach (var notificationId in unreadIds)
            {
                await _notifications.AddReadMarkerAsync(new NotificationRead(notificationId, userId), cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result.Failure($"Error marking all notifications read: {ex.Message}");
        }
    }
}
