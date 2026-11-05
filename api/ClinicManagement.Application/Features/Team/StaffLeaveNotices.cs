using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Team;

/// <summary>What the bell and the e-mail say about a congé — one wording for both, so they cannot drift.</summary>
public static class StaffLeaveNotices
{
    private const string NoBreakSpace = " ";

    public static string Period(StaffLeaveRequest leave) =>
        FrenchDayPeriod.Format(leave.StartDate, leave.EndDate) + (leave.HalfDay ? " (demi-journée)" : string.Empty);

    private static string Quote(string text) => $"«{NoBreakSpace}{text}{NoBreakSpace}»";

    public static (string Title, string Message) Requested(string requesterName, StaffLeaveRequest leave) =>
        ("Demande de congé",
            $"{requesterName} demande un congé {Period(leave)} ({FrenchDayPeriod.Days(leave.RequestedDays)}).");

    public static (string Title, string Message) Approved(StaffLeaveRequest leave) =>
        ("Congé accepté",
            $"Votre congé {Period(leave)} est accepté ({FrenchDayPeriod.Days(leave.CountedDays ?? leave.RequestedDays)}).");

    // A half day's period already says « (demi-journée) », so the count would only repeat it.
    public static (string Title, string Message) Rejected(StaffLeaveRequest leave) =>
        ("Congé refusé",
            $"Votre demande de congé {Period(leave)}{(leave.HalfDay ? string.Empty : $" ({FrenchDayPeriod.Days(leave.RequestedDays)})")} est refusée : {Quote(leave.RejectionReason ?? string.Empty)}.");

    public static (string Title, string Message) CancelledByOwner(string requesterName, StaffLeaveRequest leave) =>
        ("Congé annulé", $"{requesterName} a annulé son congé {Period(leave)}.");

    public static (string Title, string Message) CancelledByAdmin(string adminName, StaffLeaveRequest leave) =>
        ("Congé annulé", $"Votre congé {Period(leave)} a été annulé par {adminName}.");

    public static (string Title, string Message) RecordedByAdmin(StaffLeaveRequest leave) =>
        ("Absence enregistrée",
            $"Une absence ({StaffLeaveRules.TypeLabel(leave.Type).ToLowerInvariant()}) {Period(leave)} a été enregistrée pour vous.");

    public static string EmailSubject(StaffLeaveRequest leave) =>
        leave.Status == StaffLeaveStatus.Approved
            ? $"Congé accepté : {Period(leave)}"
            : $"Congé refusé : {Period(leave)}";

    public static EmailContent Email(
        StaffLeaveRequest leave, string clinicName, string recipientName, string deciderName, decimal? remaining,
        string? link)
    {
        var approved = leave.Status == StaffLeaveStatus.Approved;
        var type = StaffLeaveRules.TypeLabel(leave.Type);
        var details = new List<EmailDetail>
        {
            new("Cabinet", clinicName),
            new("Type", type),
            new("Dates", Period(leave)),
        };
        if (approved)
        {
            details.Add(new EmailDetail("Jours décomptés", FrenchDayPeriod.Days(leave.CountedDays ?? leave.RequestedDays)));
            if (remaining is { } left)
                details.Add(new EmailDetail($"Congé payé restant en {leave.StartDate.Year}", FrenchDayPeriod.Days(left)));
        }
        else
        {
            details.Add(new EmailDetail("Jours demandés", FrenchDayPeriod.Days(leave.RequestedDays)));
            details.Add(new EmailDetail("Motif du refus", leave.RejectionReason ?? string.Empty));
        }

        return new EmailContent
        {
            Title = approved ? "Congé accepté" : "Congé refusé",
            Preheader = approved
                ? $"Votre congé {Period(leave)} est accepté."
                : $"Votre demande de congé {Period(leave)} n'est pas acceptée.",
            Greeting = $"Bonjour {recipientName},",
            Intro =
            [
                approved
                    ? $"{deciderName} a accepté votre demande ({type.ToLowerInvariant()}) {Period(leave)}."
                    : $"{deciderName} n'a pas accepté votre demande ({type.ToLowerInvariant()}) {Period(leave)}."
            ],
            Details = details,
            Action = link is null ? null : new EmailAction("Voir mes congés", link),
        };
    }

    /// <summary>Best-effort, after the commit: a failed e-mail is logged and never undoes the decision.</summary>
    public static async Task SendDecisionAsync(
        ITransactionalEmailSender emailSender,
        IPublicAppUrlProvider appUrl,
        ILogger logger,
        StaffLeaveRequest leave,
        User recipient,
        string clinicName,
        string deciderName,
        decimal? remaining,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipient.Email) || !emailSender.IsConfigured)
            return;
        try
        {
            var link = appUrl.IsConfigured ? $"{appUrl.BaseUrl}/conges" : null;
            var content = Email(leave, clinicName, StaffLeaveRules.DisplayName(recipient), deciderName, remaining, link);
            var sent = await emailSender.SendAsync(recipient.Email!, EmailSubject(leave), content, cancellationToken);
            if (sent.Outcome != TransactionalEmailOutcome.Sent)
                logger.LogWarning(
                    "Congé decision e-mail not sent to user {UserId}: {Outcome} {Reason}", recipient.Id, sent.Outcome, sent.Error);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Congé decision e-mail failed for user {UserId}.", recipient.Id);
        }
    }

    /// <summary>Best-effort, after the commit: the bell row never undoes the save it announces.</summary>
    public static async Task NotifyAsync(
        INotificationGenerator notifications,
        ILogger logger,
        Guid clinicId,
        NotificationCategory category,
        (string Title, string Message) notice,
        string actorUserId,
        string? targetUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await notifications.StaffLeaveAsync(
                clinicId, category, notice.Title, notice.Message, actorUserId, targetUserId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Congé notification failed for clinic {ClinicId}.", clinicId);
        }
    }
}
