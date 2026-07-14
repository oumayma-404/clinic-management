using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

/// <summary>
/// A staff-notification feed row, annotated with the current viewer's read state. <see cref="CreatedAt"/>
/// is the notification's effective feed time (creation time for immediate categories, due time for a
/// reminder) — the value the panel orders by and renders as a relative timestamp.
/// </summary>
public class NotificationDto
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
    public string TargetKind { get; set; } = string.Empty;
    public Guid? AppointmentId { get; set; }
    public Guid? StockItemId { get; set; }
}

public static class NotificationMappingExtensions
{
    /// <summary>Maps a notification to its DTO for a specific viewer. <paramref name="isRead"/> is the
    /// per-viewer read state (read marker present, or effective before the viewer's join baseline).</summary>
    public static NotificationDto ToDto(this StaffNotification notification, bool isRead) => new()
    {
        Id = notification.Id,
        Category = notification.Category.ToString(),
        Title = notification.Title,
        Message = notification.Message,
        CreatedAt = notification.EffectiveFeedTime,
        IsRead = isRead,
        TargetKind = notification.TargetKind.ToString(),
        AppointmentId = notification.AppointmentId,
        StockItemId = notification.StockItemId
    };
}
