using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Queues the phone notification a staff chat message rings (staff-chat Part 7). Post-commit and best-effort, like
/// every other side effect of a saved message: it never throws and never fails the send.
/// </summary>
public interface IStaffChatPushQueue
{
    /// <param name="recipients">Who the message rings — <c>StaffConversation.AlertsFor</c>, the same people the screen rings.</param>
    Task QueueAsync(StaffMessage message, IReadOnlyCollection<string> recipients, CancellationToken cancellationToken = default);
}
