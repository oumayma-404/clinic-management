using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Notifications.Commands;

/// <summary>
/// Clears everything currently in the caller's own bell. Idempotent; writes one
/// <see cref="NotificationDismissal"/> per visible row and nothing else.
///
/// <para>⚠️ <b>It clears what is VISIBLE, not what is unread</b>, and that is the whole difference from
/// <c>MarkAllNotificationsReadCommand</c> beside it. The panel lists read rows too, so clearing only the unread
/// ones would empty the badge and leave a list of rows standing — « Tout effacer » that visibly does not.</para>
///
/// <para>⚠️ It is bounded by what the reader can see rather than by the panel's 50-row display cap: the cap is a
/// rendering choice, and a « Tout effacer » that left row 51 behind would be a different action from the one its
/// label names.</para>
/// </summary>
public class DismissAllNotificationsCommand : IRequest<Result>
{
}

public class DismissAllNotificationsCommandHandler : IRequestHandler<DismissAllNotificationsCommand, Result>
{
    private readonly IStaffNotificationRepository _notifications;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public DismissAllNotificationsCommandHandler(
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

    public async Task<Result> Handle(DismissAllNotificationsCommand request, CancellationToken cancellationToken)
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
            // Id-only projection, and already free of rows this user has dismissed before — so a second press
            // writes nothing rather than colliding on the composite key.
            var visibleIds = await _notifications.GetVisibleIdsForUserAsync(user.ClinicId, userId, now, cancellationToken);

            if (visibleIds.Count == 0)
            {
                return Result.Success();
            }

            foreach (var notificationId in visibleIds)
            {
                await _notifications.AddDismissalAsync(new NotificationDismissal(notificationId, userId), cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result.Failure(ErrorMessages.Generic, ex);
        }
    }
}
