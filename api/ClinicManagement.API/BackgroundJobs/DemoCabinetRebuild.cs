using ClinicManagement.Application.Common.Maintenance;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>The steps of one demo rebuild; <see cref="DemoCabinetRebuild"/> owns their order.</summary>
public interface IDemoRebuildSteps
{
    /// <summary>The cabinet this rebuild creates, known before anything is written so a failure can always remove it.</summary>
    Guid ClinicId { get; }

    Task<IReadOnlySet<string>> SchemaBaselineAsync(CancellationToken cancellationToken);

    /// <summary>Creates the cabinet with its dentist-admin switched off, so no visitor can enter it yet.</summary>
    Task ProvisionAsync(CancellationToken cancellationToken);

    Task BuildAsync(CancellationToken cancellationToken);

    /// <summary>What the money and schema checks found wrong after the build; empty when it may open.</summary>
    Task<IReadOnlyList<string>> CheckAsync(IReadOnlySet<string> schemaBaseline, CancellationToken cancellationToken);

    /// <summary>Switches the dentist-admin on: from here new visitors enter this cabinet.</summary>
    Task OpenAsync(CancellationToken cancellationToken);

    Task RetireOthersAsync(Guid currentClinicId, CancellationToken cancellationToken);

    Task PurgeAsync(Guid clinicId, CancellationToken cancellationToken);
}

/// <summary>
/// One demo rebuild (<c>demo-cabinet</c> FR-7, AC-7.6, EC-7): built aside, checked, opened, then the cabinets nobody is
/// inside are removed. Any failure before the opening removes the half-built cabinet and leaves yesterday's open.
/// </summary>
public static class DemoCabinetRebuild
{
    public static async Task RunAsync(IDemoRebuildSteps steps, ILogger logger, CancellationToken cancellationToken)
    {
        var step = "baseline";
        var written = false;
        try
        {
            var baseline = await steps.SchemaBaselineAsync(cancellationToken);
            step = "provision";
            written = true;
            await steps.ProvisionAsync(cancellationToken);
            step = "build";
            await steps.BuildAsync(cancellationToken);
            step = "check";
            var failures = await steps.CheckAsync(baseline, cancellationToken);
            if (failures.Count > 0)
            {
                throw new CheckFailedException(failures);
            }

            step = "open";
            await steps.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Demo: the rebuild failed at « {Step} »; the current cabinet stays open.", step);
            if (written)
            {
                await PurgeQuietlyAsync(steps, steps.ClinicId, logger, cancellationToken);
            }

            throw;
        }

        var current = steps.ClinicId;
        try
        {
            await steps.RetireOthersAsync(current, cancellationToken);
        }
        catch (Exception ex)
        {
            // The new cabinet is open and correct; an old one left behind is retried by the next rebuild.
            logger.LogError(ex, "Demo: cabinet {ClinicId} is open but older cabinets could not be removed.", current);
        }
    }

    /// <summary>Money drift anywhere (the demo database holds only demo cabinets) or schema drift the build added.</summary>
    public static IReadOnlyList<string> Failures(
        MoneyReconciliationReport money, IReadOnlySet<string> schemaBaseline, SchemaVerificationReport schemaAfter) =>
        money.Findings
            .Where(f => f.Severity == MoneyReconciliationSeverity.Drift)
            .Select(f => $"money {f.Check}: {f.Detail}")
            .Concat(SchemaDrift(schemaAfter).Where(d => !schemaBaseline.Contains(d)).Select(d => $"schema {d}"))
            .ToList();

    public static IReadOnlySet<string> SchemaDrift(SchemaVerificationReport report) =>
        report.Findings
            .Where(f => f.Severity == SchemaVerificationSeverity.Drift)
            .Select(f => $"{f.Scope} {f.Check}: {f.Detail}")
            .ToHashSet(StringComparer.Ordinal);

    private static async Task PurgeQuietlyAsync(IDemoRebuildSteps steps, Guid clinicId, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await steps.PurgeAsync(clinicId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Demo: the half-built cabinet {ClinicId} could not be removed.", clinicId);
        }
    }

    /// <summary>The rebuilt cabinet failed its money or schema check, so it was not opened.</summary>
    public sealed class CheckFailedException(IReadOnlyList<string> failures)
        : Exception("Demo: the rebuilt cabinet failed its checks — " + string.Join(" · ", failures))
    {
        public IReadOnlyList<string> Failures { get; } = failures;
    }
}
