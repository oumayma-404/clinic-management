using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Team;

/// <summary>What the bell says about the pointage's setup — one wording, read by the push label too.</summary>
public static class AttendanceNotices
{
    private static string Quote(string text) => StaffLeaveNotices.Quote(text);

    public const string SchemaToCreateTitle = "Créez votre schéma de pointage";

    public static (string Title, string Message) SchemaToCreate(string borneName) =>
        (SchemaToCreateTitle, $"La borne {Quote(borneName)} est en service : créez votre schéma pour y pointer.");

    public static (string Title, string Message) BorneInstalledForAdmins(string borneName, string ownerName) =>
        ("Borne installée", $"La borne {Quote(borneName)} est installée sur l'appareil de {ownerName}.");

    public static (string Title, string Message) BorneInstalledForOwner(string borneName, string adminName) =>
        ("Cet appareil est une borne", $"{adminName} a fait de cet appareil la borne {Quote(borneName)}.");

    public static (string Title, string Message) BorneNotReceived(string borneName, string? deviceDescription) =>
        ("Borne non reçue",
            $"La borne {Quote(borneName)} n'a pas été installée : la session de l'appareil choisi"
            + (deviceDescription is null ? string.Empty : $" ({deviceDescription})")
            + " a été fermée. Choisissez un autre appareil.");

    public static (string Title, string Message) SchemaBlocked(string personName, string until) =>
        ("Schéma bloqué", $"Le schéma de {personName} est bloqué jusqu'à {until} après {User.MaxSchemaFailures} essais.");
}

/// <summary>
/// Sends the pointage's setup rows, post-commit and best-effort: a failure is logged and never fails the command that
/// already committed (the generator swallows its own; the reads around it are caught here).
/// </summary>
public sealed class AttendanceNotifier
{
    private readonly INotificationGenerator _notifications;
    private readonly IUserRepository _users;
    private readonly IAttendanceTerminalRepository _terminals;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<AttendanceNotifier> _logger;

    public AttendanceNotifier(
        INotificationGenerator notifications, IUserRepository users, IAttendanceTerminalRepository terminals,
        ITenantScope tenantScope, ILogger<AttendanceNotifier> logger)
    {
        _notifications = notifications;
        _users = users;
        _terminals = terminals;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    /// <summary>A borne went live: every tracked person still without a schéma is told, on her phone too.</summary>
    public async Task BorneLiveAsync(Guid clinicId, string borneName, CancellationToken cancellationToken)
    {
        try
        {
            var roster = await StaffLeaveRules.RosterAsync(_users, clinicId, cancellationToken);
            foreach (var person in AttendanceSetupRules.TrackedPeople(roster.Values).Where(u => !u.HasAttendanceSchema))
            {
                var (title, message) = AttendanceNotices.SchemaToCreate(borneName);
                await _notifications.AttendanceNoticeAsync(
                    clinicId, NotificationCategory.AttendanceSchemaToCreate, title, message, person.Id, null, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pointage: « créez votre schéma » not sent for clinic {ClinicId}", clinicId);
        }
    }

    /// <summary>The chosen device picked its borne up: the admins are told, and so is the device's owner.</summary>
    public async Task BorneInstalledAsync(AttendanceTerminal terminal, User owner, CancellationToken cancellationToken)
    {
        try
        {
            var (adminTitle, adminMessage) = AttendanceNotices.BorneInstalledForAdmins(terminal.Name, StaffLeaveRules.DisplayName(owner));
            await _notifications.AttendanceNoticeAsync(
                terminal.ClinicId, NotificationCategory.AttendanceBorneInstalled, adminTitle, adminMessage, null, owner.Id,
                cancellationToken);

            if (owner.Id != terminal.CreatedByUserId
                && await _users.GetByIdAsync(terminal.CreatedByUserId, cancellationToken) is { } admin)
            {
                var (title, message) = AttendanceNotices.BorneInstalledForOwner(terminal.Name, StaffLeaveRules.DisplayName(admin));
                await _notifications.AttendanceNoticeAsync(
                    terminal.ClinicId, NotificationCategory.AttendanceBorneInstalled, title, message, owner.Id, null,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pointage: « borne installée » not sent for borne {TerminalId}", terminal.Id);
        }
    }

    /// <summary>A fifth wrong schéma: the admins, who can lift the block, are told until when it holds.</summary>
    public async Task SchemaBlockedAsync(Guid clinicId, User person, DateTime lockedUntilUtc, CancellationToken cancellationToken)
    {
        var (title, message) = AttendanceNotices.SchemaBlocked(
            StaffLeaveRules.DisplayName(person), AttendanceRules.LocalTime(lockedUntilUtc));
        await _notifications.AttendanceNoticeAsync(
            clinicId, NotificationCategory.AttendanceSchemaBlocked, title, message, null, null, cancellationToken);
    }

    /// <summary>
    /// A session ended (sign-out, or « Mes appareils »): a borne still waiting for it will never be picked up, so the
    /// admins are told. An anonymous sign-out has no cabinet in scope yet; the account's own one is taken.
    /// </summary>
    public async Task SessionEndedAsync(SessionFamily session, CancellationToken cancellationToken)
    {
        try
        {
            var owner = await _users.GetByIdAsync(session.UserId, cancellationToken);
            if (owner is null)
                return;
            if (_tenantScope.Kind == TenantScopeKind.Unset)
                _tenantScope.UseClinic(owner.ClinicId);
            else if (_tenantScope.Kind == TenantScopeKind.Clinic && _tenantScope.ClinicId != owner.ClinicId)
                return;

            if (await _terminals.FindPendingForSessionAsync(owner.ClinicId, session.Id, cancellationToken) is not { } waiting)
                return;
            var (title, message) = AttendanceNotices.BorneNotReceived(waiting.Name, waiting.DeviceDescription);
            await _notifications.AttendanceNoticeAsync(
                owner.ClinicId, NotificationCategory.AttendanceBorneNotReceived, title, message, null, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pointage: « borne non reçue » not checked for session {SessionId}", session.Id);
        }
    }
}
