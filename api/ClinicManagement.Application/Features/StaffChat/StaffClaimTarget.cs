using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.StaffChat;

/// <summary>What « Je m'en occupe » and « Je ne peux plus » share: finding the message, and telling its thread.</summary>
internal static class StaffClaimTarget
{
    /// <summary>The message and its thread, if the caller reads that thread — otherwise nothing, never another cabinet's.</summary>
    public static async Task<(StaffMessage Message, StaffConversation Conversation)?> FindAsync(
        IStaffMessageRepository messages, Guid messageId, StaffChatRoster roster, CancellationToken cancellationToken)
    {
        var message = await messages.GetByIdAsync(messageId, cancellationToken);
        if (message is null || message.ClinicId != roster.ClinicId)
        {
            return null;
        }

        var conversation = StaffChatInbox.Resolve(message.ConversationKey, roster);
        return conversation is null ? null : (message, conversation);
    }

    /// <summary>Every member of the thread sees « Pris par … » appear or go (AC-5.1).</summary>
    public static async Task SignalAsync(
        IRealtimeNotifier realtime, StaffChatRoster roster, StaffConversation conversation, StaffMessage message,
        ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await realtime.NotifyStaffChatAsync(roster.ClinicId, conversation.Members(roster.Members), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Claim on staff message {MessageId} saved but its thread was not signalled", message.Id);
        }
    }
}
