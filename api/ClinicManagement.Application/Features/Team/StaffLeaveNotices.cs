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
    // Her « Mes congés », and the admin's « Utilisateurs → Congés » tab (web/lib/staff-leave.ts USERS_LEAVE_TAB).
    private const string SecretaryPath = "/conges";
    private const string AdminPath = "/users?onglet=conges";

    private const string NoBreakSpace = " ";

    /// <summary>« du 16 au 18 novembre », « le 16 novembre matin », « le 16 novembre (demi-journée) » for an unqualified half.</summary>
    public static string Period(StaffLeaveRequest leave) =>
        FrenchDayPeriod.Words(leave.StartDate, leave.EndDate) + (leave.HalfDay
            ? leave.HalfDayPart switch
            {
                StaffLeaveHalfDayPart.Morning => " matin",
                StaffLeaveHalfDayPart.Afternoon => " après-midi",
                _ => " (demi-journée)",
            }
            : string.Empty);

    /// <summary>A quoted value between French quotes and no-break spaces — congés and pointage alike.</summary>
    internal static string Quote(string text) => $"«{NoBreakSpace}{text}{NoBreakSpace}»";

    public static (string Title, string Message) Requested(string requesterName, StaffLeaveRequest leave) =>
        ("Demande de congé",
            $"{requesterName} demande un congé {Period(leave)} ({FrenchDayPeriod.Days(leave.RequestedDays)}).");

    // Every date is granted; only a congé payé takes days off her balance, and those are what is named.
    public static (string Title, string Message) Approved(StaffLeaveRequest leave) =>
        ("Congé accepté",
            leave.Type == StaffLeaveType.PaidLeave
                ? $"Votre congé {Period(leave)} est accordé · {Withdrawn(leave.CountedDays ?? leave.RequestedDays)}."
                : $"Votre congé {Period(leave)} est accordé.");

    // A half day's period already says which half, so the count would only repeat it.
    public static (string Title, string Message) Rejected(StaffLeaveRequest leave) =>
        ("Congé refusé",
            $"Votre demande de congé {Period(leave)}{(leave.HalfDay ? string.Empty : $" ({FrenchDayPeriod.Days(leave.RequestedDays)})")} est refusée : {Quote(leave.RejectionReason ?? string.Empty)}.");

    public static (string Title, string Message) CancelledByOwner(string requesterName, StaffLeaveRequest leave) =>
        ("Congé annulé", $"{requesterName} a annulé son congé {Period(leave)}.");

    /// <summary>An admin withdrew hers: a waiting request is « retirée », an accepted congé « annulé » — with the motif.</summary>
    public static (string Title, string Message) CancelledByAdmin(string adminName, StaffLeaveRequest leave, bool wasApproved) =>
        wasApproved
            ? ("Congé annulé", $"Votre congé {Period(leave)} a été annulé par {adminName}{Motif(leave)}.")
            : ("Demande retirée", $"Votre demande de congé {Period(leave)} a été retirée par {adminName}{Motif(leave)}.");

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
            new("Dates", Capitalized(Period(leave))),
        };
        if (approved)
        {
            if (leave.Type == StaffLeaveType.PaidLeave)
                details.Add(new EmailDetail("Retirés de votre solde", FrenchDayPeriod.Days(leave.CountedDays ?? leave.RequestedDays)));
            if (remaining is { } left)
                details.Add(new EmailDetail($"Congé payé restant en {leave.StartDate.Year}", FrenchDayPeriod.Balance(left)));
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
                ? $"Votre congé {Period(leave)} est accordé."
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

    public static string RequestSubject(string requesterName, StaffLeaveRequest leave) =>
        $"Demande de congé : {requesterName}, {Period(leave)}";

    /// <summary>The e-mail each admin gets when a secretary asks — the request card's facts, and a way to it.</summary>
    public static EmailContent RequestEmail(
        StaffLeaveRequest leave, string clinicName, string recipientName, string requesterName, string? link)
    {
        var type = StaffLeaveRules.TypeLabel(leave.Type);
        return new EmailContent
        {
            Title = "Demande de congé",
            Preheader = $"{requesterName} demande un congé {Period(leave)}.",
            Greeting = $"Bonjour {recipientName},",
            Intro = [$"{requesterName} demande un congé ({type.ToLowerInvariant()}) {Period(leave)}."],
            Details =
            [
                new("Cabinet", clinicName),
                new("Secrétaire", requesterName),
                new("Type", type),
                new("Dates", Capitalized(Period(leave))),
                new("Jours demandés", FrenchDayPeriod.Days(leave.RequestedDays)),
                new("Motif", leave.Reason),
            ],
            Action = link is null ? null : new EmailAction("Voir la demande", link),
        };
    }

    public static string CancellationSubject(StaffLeaveRequest leave, bool wasApproved) =>
        wasApproved ? $"Congé annulé : {Period(leave)}" : $"Demande retirée : {Period(leave)}";

    /// <summary>The e-mail she gets when an admin withdraws her waiting request or her accepted congé.</summary>
    public static EmailContent CancellationEmail(
        StaffLeaveRequest leave, bool wasApproved, string clinicName, string recipientName, string adminName, string? link)
    {
        var type = StaffLeaveRules.TypeLabel(leave.Type);
        var details = new List<EmailDetail>
        {
            new("Cabinet", clinicName),
            new("Type", type),
            new("Dates", Capitalized(Period(leave))),
        };
        if (!string.IsNullOrWhiteSpace(leave.CancelReason))
            details.Add(new EmailDetail("Motif", leave.CancelReason));

        var (title, _) = CancelledByAdmin(adminName, leave, wasApproved);
        return new EmailContent
        {
            Title = title,
            Preheader = wasApproved
                ? $"Votre congé {Period(leave)} a été annulé."
                : $"Votre demande de congé {Period(leave)} a été retirée.",
            Greeting = $"Bonjour {recipientName},",
            Intro =
            [
                wasApproved
                    ? $"{adminName} a annulé votre congé ({type.ToLowerInvariant()}) {Period(leave)}."
                    : $"{adminName} a retiré votre demande ({type.ToLowerInvariant()}) {Period(leave)}."
            ],
            Details = details,
            Action = link is null ? null : new EmailAction("Voir mes congés", link),
        };
    }

    /// <summary>Best-effort, after the commit: a failed e-mail is logged and never undoes the decision.</summary>
    public static Task SendDecisionAsync(
        ITransactionalEmailSender emailSender,
        IPublicAppUrlProvider appUrl,
        ILogger logger,
        StaffLeaveRequest leave,
        User recipient,
        string clinicName,
        string deciderName,
        decimal? remaining,
        CancellationToken cancellationToken) =>
        SendAsync(
            emailSender, appUrl, logger, recipient, EmailSubject(leave), SecretaryPath,
            link => Email(leave, clinicName, StaffLeaveRules.DisplayName(recipient), deciderName, remaining, link),
            cancellationToken);

    /// <summary>Best-effort, after the commit: one e-mail per active admin; a failed one never undoes the request.</summary>
    public static async Task SendRequestAsync(
        ITransactionalEmailSender emailSender,
        IPublicAppUrlProvider appUrl,
        ILogger logger,
        StaffLeaveRequest leave,
        IEnumerable<User> roster,
        string requesterName,
        string clinicName,
        CancellationToken cancellationToken)
    {
        foreach (var admin in roster.Where(u => u.IsAdmin() && u.IsActive && u.Id != leave.UserId))
        {
            await SendAsync(
                emailSender, appUrl, logger, admin, RequestSubject(requesterName, leave), AdminPath,
                link => RequestEmail(leave, clinicName, StaffLeaveRules.DisplayName(admin), requesterName, link),
                cancellationToken);
        }
    }

    /// <summary>Best-effort, after the commit: a failed e-mail is logged and never undoes the withdrawal.</summary>
    public static Task SendCancellationAsync(
        ITransactionalEmailSender emailSender,
        IPublicAppUrlProvider appUrl,
        ILogger logger,
        StaffLeaveRequest leave,
        bool wasApproved,
        User recipient,
        string clinicName,
        string adminName,
        CancellationToken cancellationToken) =>
        SendAsync(
            emailSender, appUrl, logger, recipient, CancellationSubject(leave, wasApproved), SecretaryPath,
            link => CancellationEmail(leave, wasApproved, clinicName, StaffLeaveRules.DisplayName(recipient), adminName, link),
            cancellationToken);

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

    private static async Task SendAsync(
        ITransactionalEmailSender emailSender,
        IPublicAppUrlProvider appUrl,
        ILogger logger,
        User recipient,
        string subject,
        string path,
        Func<string?, EmailContent> content,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipient.Email) || !emailSender.IsConfigured)
            return;
        try
        {
            var link = appUrl.IsConfigured ? $"{appUrl.BaseUrl}{path}" : null;
            var sent = await emailSender.SendAsync(recipient.Email!, subject, content(link), cancellationToken);
            if (sent.Outcome != TransactionalEmailOutcome.Sent)
                logger.LogWarning(
                    "Congé e-mail not sent to user {UserId}: {Outcome} {Reason}", recipient.Id, sent.Outcome, sent.Error);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Congé e-mail failed for user {UserId}.", recipient.Id);
        }
    }

    /// <summary>« 2,5 j retirés de votre solde », « 1 j retiré de votre solde ».</summary>
    private static string Withdrawn(decimal days) =>
        $"{FrenchDayPeriod.Short(days)} {(Math.Abs(days) > 1 ? "retirés" : "retiré")} de votre solde";

    private static string Motif(StaffLeaveRequest leave) =>
        string.IsNullOrWhiteSpace(leave.CancelReason) ? string.Empty : $" : {Quote(leave.CancelReason)}";

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
