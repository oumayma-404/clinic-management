using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Relay;

/// <summary>The PC de secours's rows in « Journal d'activité » (FR-8, AC-5.5, AC-7.2, AC-8.5). Staged; the caller saves.</summary>
public static class RelayJournal
{
    public const string EntityType = nameof(ClinicRelay);

    public static Task StageAsync(
        IAuditEntryRepository auditEntries, AuditActor actor, ClinicRelay relay, AuditAction action, string what,
        DateTime occurredAtUtc, CancellationToken cancellationToken) =>
        auditEntries.AddRangeAsync(new[]
        {
            new AuditEntry(relay.ClinicId, actor.UserId, actor.Email, EntityType, relay.Id.ToString(), action,
                $"{what} — {relay.Label}", occurredAtUtc),
        }, cancellationToken);

    /// <summary>A row written on the PC itself, which holds no <see cref="ClinicRelay"/> (the cloud owns those).</summary>
    public static Task StageOnPcAsync(
        IAuditEntryRepository auditEntries, AuditActor actor, Guid clinicId, Guid relayId, string label, AuditAction action,
        string what, DateTime occurredAtUtc, CancellationToken cancellationToken) =>
        auditEntries.AddRangeAsync(new[]
        {
            new AuditEntry(clinicId, actor.UserId, actor.Email, EntityType, relayId.ToString(), action,
                $"{what} — {label}", occurredAtUtc),
        }, cancellationToken);

    public const string Setup = "PC de secours installé";
    public const string FirstCopy = "Première copie terminée";
    public const string Retire = "PC de secours retiré";
    public const string Lost = "PC de secours déclaré perdu ou volé";
    public const string Abandoned = "Installation du PC de secours abandonnée";
    public const string Uninstalled = "PC de secours désinstallé";
    public const string Erased = "Copie du cabinet effacée du PC de secours";
    public const string Promoted = "PC de secours promu en serveur local";
}
