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
    public const string TookOver = "Le PC de secours a pris le relais (le cloud ne répondait plus)";
    public const string Reclaimed = "Le cloud a repris la main sur le PC de secours";
    public const string Returned = "Le travail fait sur le PC de secours pendant la coupure est revenu dans le cloud";

    /// <summary>AC-9.4: what a restore had lost, given back by the PC de secours.</summary>
    public static string GapReturned(int count) =>
        count == 1
            ? "Le cloud restauré a récupéré 1 enregistrement du PC de secours"
            : $"Le cloud restauré a récupéré {count} enregistrements du PC de secours";

    /// <summary>US-7 / AC-7.3: what an overruled PC held, listed for re-entry on the cloud.</summary>
    public static string ListedToReEnter(int count) =>
        count == 1
            ? "1 enregistrement fait sur le PC de secours pendant la coupure est listé « À reprendre »"
            : $"{count} enregistrements faits sur le PC de secours pendant la coupure sont listés « À reprendre »";

    /// <summary>AC-5.5: what marks, in the cloud's journal, a row the cabinet wrote on its PC de secours during a cut.</summary>
    public const string ViaRelay = "Via PC de secours";

    public static string MarkViaRelay(string? changedFields) =>
        string.IsNullOrWhiteSpace(changedFields) ? ViaRelay : $"{ViaRelay} · {changedFields}";

    public const string ReclaimedByDevices =
        "Le cloud a repris la main : les appareils du cabinet le joignaient, mais plus le PC de secours";
}
