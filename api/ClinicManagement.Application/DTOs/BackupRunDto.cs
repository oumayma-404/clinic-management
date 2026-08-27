using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One recorded backup attempt (L4d) — the row « Paramètres » shows and the history endpoint pages over.
/// </summary>
public sealed record BackupRunDto
{
    public required Guid Id { get; init; }
    public required DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }

    /// <summary>`running` | `succeeded` | `failed` — the wire form, lowercased like every other status here.</summary>
    public required string Outcome { get; init; }

    /// <summary>`scheduled` | `manual`. Kept because « personne n'a cliqué » and « le programme n'a pas tourné »
    /// are two different problems and the history list is where they are told apart.</summary>
    public required string Trigger { get; init; }

    public string? DestinationPath { get; init; }
    public long? SizeBytes { get; init; }

    /// <summary>
    /// Objects found by <c>pg_restore --list</c> (L4c). Surfaced rather than kept in the log because « 3 objets »
    /// where the schema has thirty-eight tables is the one shape of disaster a green tick cannot express.
    /// </summary>
    public int? VerifiedObjectCount { get; init; }

    public string? Error { get; init; }

    public static BackupRunDto From(BackupRun run) => new()
    {
        Id = run.Id,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        Outcome = run.Outcome switch
        {
            BackupOutcome.Succeeded => BackupRunOutcomes.Succeeded,
            BackupOutcome.Failed => BackupRunOutcomes.Failed,
            _ => BackupRunOutcomes.Running,
        },
        Trigger = run.Trigger == BackupRun.TriggerManual ? BackupRunTriggers.Manual : BackupRunTriggers.Scheduled,
        DestinationPath = run.DestinationPath,
        SizeBytes = run.SizeBytes,
        VerifiedObjectCount = run.VerifiedObjectCount,
        Error = string.IsNullOrWhiteSpace(run.Error) ? null : run.Error,
    };
}

/// <summary>The wire values of <see cref="BackupRunDto.Outcome"/> — one place, so the client cannot guess.</summary>
public static class BackupRunOutcomes
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

/// <summary>The wire values of <see cref="BackupRunDto.Trigger"/>.</summary>
public static class BackupRunTriggers
{
    public const string Scheduled = "scheduled";
    public const string Manual = "manual";
}
