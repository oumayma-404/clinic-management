using System.Globalization;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.StaffChat.Queries;

/// <summary>One thread, newest first, 50 at a time; <see cref="Before"/> is the previous page's <c>NextCursor</c>.</summary>
public class GetStaffMessagesQuery : IRequest<Result<StaffMessagePageDto>>
{
    public const int DefaultTake = 50;
    public const int MaxTake = 100;

    public string Conversation { get; set; } = string.Empty;
    public string? Before { get; set; }
    public int? Take { get; set; }
}

public class GetStaffMessagesQueryHandler : IRequestHandler<GetStaffMessagesQuery, Result<StaffMessagePageDto>>
{
    private readonly IStaffMessageRepository _messages;
    private readonly IPatientRepository _patients;
    private readonly IUserRepository _users;
    private readonly IDoctorRepository _doctors;
    private readonly IClinicContext _clinicContext;

    public GetStaffMessagesQueryHandler(
        IStaffMessageRepository messages,
        IPatientRepository patients,
        IUserRepository users,
        IDoctorRepository doctors,
        IClinicContext clinicContext)
    {
        _messages = messages;
        _patients = patients;
        _users = users;
        _doctors = doctors;
        _clinicContext = clinicContext;
    }

    public async Task<Result<StaffMessagePageDto>> Handle(GetStaffMessagesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await StaffChatRoster.LoadForCallerAsync(_clinicContext, _users, _doctors, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result<StaffMessagePageDto>.FailureFrom(loaded);
            }

            var roster = loaded.Value!;
            var conversation = StaffChatInbox.Resolve(request.Conversation, roster);
            if (conversation is null)
            {
                return Result<StaffMessagePageDto>.Failure(
                    StaffChatRefusals.ConversationNotFound, StaffChatRefusals.ConversationNotFoundCode);
            }

            var take = Math.Clamp(request.Take ?? GetStaffMessagesQuery.DefaultTake, 1, GetStaffMessagesQuery.MaxTake);
            var (beforeSentAt, beforeId) = StaffMessageCursor.Parse(request.Before);

            var page = await _messages.GetPageAsync(
                roster.ClinicId, conversation.Key, beforeSentAt, beforeId, take, cancellationToken);

            var markers = await _messages.GetMarkersAsync(roster.ClinicId, conversation.Key, cancellationToken);

            var pairs = page.Select(m => (Message: m, Dto: StaffChatInbox.ToMessageDto(m, roster, conversation, markers))).ToList();
            await StaffMessageLinks.FillAsync(pairs, roster, _patients, _messages, cancellationToken);

            return Result<StaffMessagePageDto>.Success(new StaffMessagePageDto
            {
                Items = pairs.Select(p => p.Dto).ToList(),
                NextCursor = page.Count == take ? StaffMessageCursor.Of(page[^1]) : null,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffMessagePageDto>.Failure("Impossible de charger les messages.", ex);
        }
    }
}

/// <summary>A page boundary: the (SentAt, Id) of the oldest message shown. An unreadable cursor starts from the newest.</summary>
public static class StaffMessageCursor
{
    public static string Of(StaffMessage message) =>
        $"{message.SentAt.Ticks.ToString(CultureInfo.InvariantCulture)}_{message.Id:N}";

    public static (DateTime? SentAt, Guid? Id) Parse(string? cursor)
    {
        var parts = cursor?.Split('_');
        if (parts is not { Length: 2 }
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks
            || !Guid.TryParseExact(parts[1], "N", out var id))
        {
            return (null, null);
        }

        return (new DateTime(ticks, DateTimeKind.Utc), id);
    }
}
