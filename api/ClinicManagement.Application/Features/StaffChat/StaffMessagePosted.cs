using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.StaffChat;

/// <summary>What every saved message does next, typed or announced: wake its thread's tabs, then answer with its links.</summary>
internal static class StaffMessagePosted
{
    public static async Task<StaffMessageDto> AnnounceAsync(
        StaffMessage message, StaffConversation conversation, StaffChatRoster roster, IRealtimeNotifier realtime,
        IPatientRepository patients, IStaffMessageRepository messages, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await realtime.NotifyStaffChatAsync(roster.ClinicId, conversation.Members(roster.Members), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Staff chat message {MessageId} saved but its members were not signalled", message.Id);
        }

        return await DtoAsync(message, roster, patients, messages, logger, cancellationToken);
    }

    /// <summary>The message with its links; a link that cannot be read back never turns a committed write into a failure.</summary>
    public static async Task<StaffMessageDto> DtoAsync(
        StaffMessage message, StaffChatRoster roster, IPatientRepository patients, IStaffMessageRepository messages,
        ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            return await StaffMessageLinks.ToDtoAsync(message, roster, patients, messages, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Staff chat message {MessageId} saved but its links could not be read back", message.Id);
            return StaffChatInbox.ToMessageDto(message, roster);
        }
    }
}
