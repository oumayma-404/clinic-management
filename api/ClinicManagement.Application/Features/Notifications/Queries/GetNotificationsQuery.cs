using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Notifications.Queries;

/// <summary>
/// Lists the current user's clinic notifications for the panel — the most recent 50 due notifications,
/// newest first, each annotated with the viewer's read state. Actor-excluded notifications (the viewer's
/// own actions) are hidden entirely; notifications effective before the viewer's join time show as read.
/// </summary>
public class GetNotificationsQuery : IRequest<Result<IEnumerable<NotificationDto>>>
{
}

public class GetNotificationsQueryHandler : IRequestHandler<GetNotificationsQuery, Result<IEnumerable<NotificationDto>>>
{
    // The panel shows at most the most-recent 50 notifications (spec US-1).
    private const int MaxRows = 50;

    private readonly IStaffNotificationRepository _notifications;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;

    public GetNotificationsQueryHandler(
        IStaffNotificationRepository notifications,
        IUserRepository userRepository,
        IClinicContext clinicContext)
    {
        _notifications = notifications;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<IEnumerable<NotificationDto>>> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<IEnumerable<NotificationDto>>.Failure("User ID not found in token");
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<IEnumerable<NotificationDto>>.Failure("User not found");
            }

            var now = DateTime.UtcNow;
            var recent = await _notifications.GetRecentForUserAsync(user.ClinicId, userId, now, MaxRows, cancellationToken);

            var ids = recent.Select(n => n.Id).ToList();
            var readIds = await _notifications.GetReadNotificationIdsAsync(userId, ids, cancellationToken);
            var readSet = new HashSet<Guid>(readIds);

            // A row counts as read for this viewer if they have a read marker, OR it is effective before
            // their join baseline (late joiners see older notifications as already-read — no day-one flood).
            var dtos = recent
                .Select(n => n.ToDto(readSet.Contains(n.Id) || n.EffectiveFeedTime < user.CreatedAt))
                .ToList();

            return Result<IEnumerable<NotificationDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<NotificationDto>>.Failure($"Error retrieving notifications: {ex.Message}");
        }
    }
}
