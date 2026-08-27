using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
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
    private readonly IStockItemRepository _stockItems;
    private readonly ISupplierRepository _suppliers;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;

    public GetNotificationsQueryHandler(
        IStaffNotificationRepository notifications,
        IStockItemRepository stockItems,
        ISupplierRepository suppliers,
        IUserRepository userRepository,
        IClinicContext clinicContext)
    {
        _notifications = notifications;
        _stockItems = stockItems;
        _suppliers = suppliers;
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
                return Result<IEnumerable<NotificationDto>>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<IEnumerable<NotificationDto>>.Failure("Utilisateur introuvable.");
            }

            var now = DateTime.UtcNow;
            var recent = await _notifications.GetRecentForUserAsync(user.ClinicId, userId, now, MaxRows, cancellationToken);

            var ids = recent.Select(n => n.Id).ToList();
            var readIds = await _notifications.GetReadNotificationIdsAsync(userId, ids, cancellationToken);
            var readSet = new HashSet<Guid>(readIds);

            var contacts = await ResolveLowStockContactsAsync(user.ClinicId, recent, cancellationToken);

            // A row counts as read for this viewer if they have a read marker, OR it is effective before
            // their join baseline (late joiners see older notifications as already-read — no day-one flood).
            var dtos = recent
                .Select(n => n.ToDto(
                    readSet.Contains(n.Id) || n.EffectiveFeedTime < user.CreatedAt,
                    n.StockItemId is { } itemId && contacts.TryGetValue(itemId, out var s) ? s : null))
                .ToList();

            return Result<IEnumerable<NotificationDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<NotificationDto>>.Failure($"Error retrieving notifications: {ex.Message}");
        }
    }

    /// <summary>
    /// « Commander chez qui ? » for each <c>LowStock</c> row on this page — the article's <b>current</b> supplier,
    /// keyed by stock-item id (AC-6, AC-7).
    /// <para>
    /// Two batched reads for the whole feed, never one per row. The chain is deliberately resolved here rather
    /// than frozen into the notification's message when it was written: an alert that fired before anybody filed
    /// the article's fournisseur becomes actionable the moment they do, and one whose article has since been
    /// re-sourced can never name the old supplier.
    /// </para>
    /// <para>
    /// A `LowStock` row whose article has since been deleted simply resolves to nothing, which is EC-3: the row
    /// keeps its stored message and quietly loses the contact line and the button.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, Supplier>> ResolveLowStockContactsAsync(
        Guid clinicId,
        IEnumerable<StaffNotification> notifications,
        CancellationToken cancellationToken)
    {
        var itemIds = notifications
            .Where(n => n.Category == NotificationCategory.LowStock && n.StockItemId.HasValue)
            .Select(n => n.StockItemId!.Value)
            .Distinct()
            .ToList();

        if (itemIds.Count == 0)
        {
            return new Dictionary<Guid, Supplier>();
        }

        var links = await _stockItems.GetSupplierLinksAsync(clinicId, itemIds, cancellationToken);
        if (links.Count == 0)
        {
            return new Dictionary<Guid, Supplier>();
        }

        var suppliers = await _suppliers.GetByIdsAsync(clinicId, links.Values.ToList(), cancellationToken);

        var byItem = new Dictionary<Guid, Supplier>(links.Count);
        foreach (var link in links)
        {
            if (suppliers.TryGetValue(link.Value, out var supplier))
            {
                byItem[link.Key] = supplier;
            }
        }

        return byItem;
    }
}
