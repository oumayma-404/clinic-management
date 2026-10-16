using ClinicManagement.Application.Common;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.StaffChat;

/// <summary>The cabinet's quick messages as one person sees them (FR-4, AC-2.2) — shared by the read and both writes.</summary>
public static class StaffQuickMessageCatalog
{
    /// <summary>How far back a person's own sends order their buttons: today and the 59 days before, in Tunis.</summary>
    public const int UsageWindowDays = 60;

    /// <summary>The saved list in the cabinet's order, or the defaults when there is none.</summary>
    public static IReadOnlyList<StaffQuickMessage> Effective(StaffQuickMessageList? list) =>
        list is null ? StaffQuickMessageDefaults.Items() : list.Items.OrderBy(i => i.SortOrder).ToList();

    public static async Task<StaffQuickMessageListDto> BuildAsync(
        StaffQuickMessageList? list, Guid clinicId, string userId, IStaffMessageRepository messages, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var since = ClinicClock.StartOfLocalDayUtc(ClinicClock.ClinicToday(nowUtc).AddDays(-(UsageWindowDays - 1)));
        var usage = await messages.GetQuickMessageUsageAsync(clinicId, userId, since, cancellationToken);

        return new StaffQuickMessageListDto
        {
            Version = list?.Version ?? 0,
            Items = Effective(list)
                .OrderByDescending(i => usage.TryGetValue(i.Key, out var count) ? count : 0)
                .ThenBy(i => i.SortOrder)
                .Select(ToDto)
                .ToList(),
        };
    }

    public static string AudienceKey(StaffQuickMessageAudience audience) => audience switch
    {
        StaffQuickMessageAudience.Dentist => "dentist",
        StaffQuickMessageAudience.Desk => "desk",
        StaffQuickMessageAudience.Reply => "reply",
        _ => throw new ArgumentOutOfRangeException(nameof(audience), audience, null),
    };

    public static bool TryParseAudience(string? value, out StaffQuickMessageAudience audience)
    {
        switch (value)
        {
            case "dentist":
                audience = StaffQuickMessageAudience.Dentist;
                return true;
            case "desk":
                audience = StaffQuickMessageAudience.Desk;
                return true;
            case "reply":
                audience = StaffQuickMessageAudience.Reply;
                return true;
            default:
                audience = default;
                return false;
        }
    }

    private static StaffQuickMessageDto ToDto(StaffQuickMessage item) => new()
    {
        Key = item.Key,
        Label = item.Label,
        For = AudienceKey(item.Audience),
        AsksForPatient = item.AsksForPatient,
        Urgent = item.IsUrgent,
        BuiltIn = item.IsBuiltIn,
        Order = item.SortOrder,
    };
}
