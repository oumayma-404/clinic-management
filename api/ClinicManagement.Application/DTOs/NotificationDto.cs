using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;

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

    /// <summary>
    /// The fournisseur to contact about this row — populated for <c>LowStock</c> rows only, and only when the
    /// article still names one (AC-6).
    /// <para>
    /// ⚠️ <b>Resolved at READ time from the article's current link, never stored on the notification</b> (AC-7).
    /// That is what makes an alert fired last week actionable the moment somebody files the article's supplier,
    /// and what stops the row naming a supplier the article no longer has. A copy frozen into
    /// <c>StaffNotification.Message</c> would be wrong in both directions and unfixable in either.
    /// </para>
    /// </summary>
    public string? SupplierName { get; set; }

    /// <summary>The same supplier's deliverable Tunisian E.164 number, or null — decides the row's WhatsApp button.</summary>
    public string? SupplierPhoneE164 { get; set; }
}

public static class NotificationMappingExtensions
{
    /// <summary>Maps a notification to its DTO for a specific viewer. <paramref name="isRead"/> is the
    /// per-viewer read state (read marker present, or effective before the viewer's join baseline).
    /// <paramref name="supplier"/> is the contact resolved for a <c>LowStock</c> row, batched by the caller.</summary>
    public static NotificationDto ToDto(
        this StaffNotification notification, bool isRead, Supplier? supplier = null) => new()
    {
        SupplierName = supplier?.Name,
        SupplierPhoneE164 = PhoneNumber.ToE164(supplier?.PhoneNumber),
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
