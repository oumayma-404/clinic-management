using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Notifications.Commands;

/// <summary>
/// Clears a single notification from the caller's own bell. Idempotent. A notification from another clinic
/// reads as "not found" (tenant-isolation convention).
///
/// <para>⚠️ <b>It writes a marker; it does not delete the row.</b> A <see cref="StaffNotification"/> with a null
/// <c>TargetUserId</c> is one row shown to every colleague, so deleting it here would clear a low-stock alert or
/// a post-visit prompt out of every other bell in the cabinet at the same time. See
/// <see cref="NotificationDismissal"/>.</para>
///
/// <para>It deliberately does <b>not</b> also mark the row read. The two are different statements and the unread
/// badge is built from the read marker — but the badge's own query excludes dismissed rows, so a cleared
/// notification stops counting either way, and « effacer » never has to pretend to be « lu ».</para>
/// </summary>
public class DismissNotificationCommand : IRequest<Result>
{
    public Guid Id { get; set; }
}

public class DismissNotificationCommandHandler : IRequestHandler<DismissNotificationCommand, Result>
{
    private readonly IStaffNotificationRepository _notifications;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DismissNotificationCommandHandler> _logger;

    public DismissNotificationCommandHandler(
        IStaffNotificationRepository notifications,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        ILogger<DismissNotificationCommandHandler> logger)
    {
        _notifications = notifications;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(DismissNotificationCommand request, CancellationToken cancellationToken)
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
            if (!await _notifications.DismissalExistsAsync(request.Id, userId, cancellationToken))
            {
                await _notifications.AddDismissalAsync(new NotificationDismissal(request.Id, userId), cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // AC-13.2: the detail goes to the log; the caller only ever sees French guidance.
            _logger.LogError(ex, "Unhandled failure dismissing notification");
            return Result.Failure("Erreur lors de la suppression de la notification.");
        }
    }
}
