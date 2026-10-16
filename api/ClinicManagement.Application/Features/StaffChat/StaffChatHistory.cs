using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.StaffChat;

/// <summary>
/// EC-14: someone who becomes a secretary joins every dentist's secretariat with its history already read — the
/// account-age floor cannot say so, since the account is older than the history it is joining.
/// </summary>
public static class StaffChatHistory
{
    /// <summary>Stages a read marker at <paramref name="nowUtc"/> on each of those secretariats; the caller saves.</summary>
    public static async Task MarkDesksReadAsync(
        IStaffMessageRepository messages, Guid clinicId, string userId, IEnumerable<string> deskOwnerUserIds,
        DateTime nowUtc, CancellationToken cancellationToken)
    {
        foreach (var owner in deskOwnerUserIds.Where(o => o != userId).Distinct(StringComparer.Ordinal))
        {
            var key = StaffConversation.Desk(owner).Key;
            var marker = await messages.GetMarkerAsync(clinicId, userId, key, cancellationToken);
            if (marker is null)
            {
                await messages.AddMarkerAsync(new StaffChatReadMarker(clinicId, userId, key, nowUtc), cancellationToken);
            }
            else
            {
                marker.Advance(nowUtc);
            }
        }
    }
}
