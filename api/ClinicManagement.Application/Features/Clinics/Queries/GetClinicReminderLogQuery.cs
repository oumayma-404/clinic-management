using MediatR;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Queries;

/// <summary>
/// The « Rappels » page: one filtered, paged view of the reminder outbox plus the clinic's three counters.
///
/// <para>Supersedes <see cref="GetClinicReminderStatusQuery"/>, which took only a <c>take</c> (20 by default) and
/// therefore could not answer the question the page exists for — « pourquoi ce patient n'a pas reçu son SMS la
/// semaine dernière ? » lived past the end of the twenty rows it returned.</para>
///
/// <para><b>Not admin-only</b>, unlike its predecessor. Reading the log is exactly what a secretary fielding
/// « je n'ai reçu aucun message » needs, and the rows carry a patient name and a masked phone — no credentials,
/// no template bodies, nothing an admin gate was protecting. <b>Writing</b> the channel settings stays admin.</para>
/// </summary>
public class GetClinicReminderLogQuery : IRequest<Result<ReminderLogDto>>
{
    /// <summary>How far back the failure counter looks. See <see cref="ReminderLogDto.FailedRecent"/>.</summary>
    public const int FailedWindowDays = 7;

    /// <summary>
    /// `sent` | `pending` | `failed` | `blocked`, or null for every status. An unknown value is ignored, not
    /// refused.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>`SMS` | `WhatsApp`, or null for every channel. An unknown value is ignored, not refused.</summary>
    public string? Channel { get; init; }

    /// <summary>Inclusive clinic-local calendar-day bounds (`yyyy-MM-dd`).</summary>
    public string? From { get; init; }
    public string? To { get; init; }

    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

public class GetClinicReminderLogQueryHandler : IRequestHandler<GetClinicReminderLogQuery, Result<ReminderLogDto>>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;

    public GetClinicReminderLogQueryHandler(
        INotificationRepository notificationRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext)
    {
        _notificationRepository = notificationRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<ReminderLogDto>> Handle(
        GetClinicReminderLogQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<ReminderLogDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var user = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (user == null)
            {
                return Result<ReminderLogDto>.Failure("Utilisateur introuvable.");
            }

            // Local-day bounds through ClinicClock, never UTC: Tunisia is UTC+1, so a UTC day boundary files a
            // send made at 00:30 local into the previous day — the § 4.1 defect, in a new counter.
            var (todayFrom, todayTo) = ClinicClock.TodayRangeUtc();
            var failedSince = todayFrom.AddDays(-GetClinicReminderLogQuery.FailedWindowDays);

            var page = await _notificationRepository.GetClinicLogAsync(
                user.ClinicId,
                ParseStatus(request.Status),
                ParseChannel(request.Channel),
                ParseLocalDayStart(request.From),
                ParseLocalDayEnd(request.To),
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            var counts = await _notificationRepository.GetClinicLogCountsAsync(
                user.ClinicId, todayFrom, todayTo, failedSince, cancellationToken);

            return Result<ReminderLogDto>.Success(new ReminderLogDto(
                new PagedResult<ReminderStatusDto>(
                    page.Items.Select(ReminderStatusMapper.ToDto).ToList(),
                    page.Page,
                    page.PageSize,
                    page.TotalCount),
                counts.SentToday,
                counts.Pending,
                counts.FailedRecent,
                counts.Blocked,
                counts.HeldByAllowance));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // French, and without the raw exception text — the A-8 class the P1/P2 sweep closed elsewhere.
            return Result<ReminderLogDto>.Failure("Erreur lors de la récupération du journal des rappels.");
        }
    }

    /*
     * The three parsers below are all TOLERANT: an unrecognised value is ignored rather than refused.
     *
     * That mirrors the lab-orders stage filter and the appointments deep-link. A stale bookmark or a renamed
     * status should show the full log, not a French error about a query parameter the user never typed.
     */

    private static NotificationStatus? ParseStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "sent" => NotificationStatus.Sent,
        "pending" => NotificationStatus.Pending,
        "failed" => NotificationStatus.Failed,
        "blocked" => NotificationStatus.Blocked,
        _ => null,
    };

    private static NotificationType? ParseChannel(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "sms" => NotificationType.SMS,
        "whatsapp" => NotificationType.WhatsApp,
        _ => null,
    };

    /// <summary>Start of a clinic-local calendar day as a UTC instant, or null when unparseable.</summary>
    private static DateTime? ParseLocalDayStart(string? value) =>
        DateTime.TryParse(value, out var day) ? ClinicClock.StartOfLocalDayUtc(day.Date) : null;

    /// <summary>
    /// The <b>last tick</b> of a clinic-local day, not the next midnight.
    ///
    /// <para><c>EndOfLocalDayUtc</c> is exclusive while this filter is inclusive on both ends, so using it would
    /// count a row scheduled exactly at midnight in two adjacent windows — finding #20, re-armed.</para>
    /// </summary>
    private static DateTime? ParseLocalDayEnd(string? value) =>
        DateTime.TryParse(value, out var day) ? ClinicClock.LastTickOfLocalDayUtc(day.Date) : null;
}
