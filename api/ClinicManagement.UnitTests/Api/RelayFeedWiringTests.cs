using ClinicManagement.UnitTests.Common;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The PC de secours's wiring (<c>clinic-pc-copy</c>), asserted against the startup sources — both halves are correct
/// in isolation and wrong only by position, which no behavioural test can see
/// (<c>MessagingCapabilityRegistrationTests</c>' technique).
/// </summary>
public class RelayFeedWiringTests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { SolutionSources.Root().FullName }.Concat(parts).ToArray()));

    // The copy loop exists only where there is a cloud to follow; anywhere else it would call a cloud that is itself.
    [Fact]
    public void The_Copy_Loop_Is_Registered_Only_On_A_Pc_De_Secours()
    {
        var program = Read("ClinicManagement.API", "Program.cs");

        var registration = program.IndexOf("AddHostedService<ClinicManagement.API.BackgroundJobs.RelayFeedJob>", StringComparison.Ordinal);
        Assert.True(registration > 0, "RelayFeedJob is no longer registered in Program.cs.");

        var gate = program.LastIndexOf("if (profile.MirrorsCloudClinic)", registration, StringComparison.Ordinal);
        Assert.True(gate > 0, "RelayFeedJob must be registered inside « if (profile.MirrorsCloudClinic) ».");
        Assert.DoesNotContain('}', program[program.IndexOf('{', gate)..registration]);
    }

    // D21: the discovery answer exists only where there is a PC to find — a cloud or a LAN server never answers it.
    [Fact]
    public void The_Discovery_Responder_Is_Registered_Only_On_A_Pc_De_Secours()
    {
        var program = Read("ClinicManagement.API", "Program.cs");

        var registration = program.IndexOf("AddHostedService<ClinicManagement.API.BackgroundJobs.RelayDiscoveryResponder>", StringComparison.Ordinal);
        Assert.True(registration > 0, "RelayDiscoveryResponder is no longer registered in Program.cs.");

        var gate = program.LastIndexOf("if (profile.MirrorsCloudClinic)", registration, StringComparison.Ordinal);
        Assert.True(gate > 0, "RelayDiscoveryResponder must be registered inside « if (profile.MirrorsCloudClinic) ».");
        Assert.DoesNotContain('}', program[program.IndexOf('{', gate)..registration]);
    }

    // The watch reads the heartbeats the cloud receives; on a PC there are none, and its bell rows come from the cloud.
    [Fact]
    public void The_Watch_Is_Registered_Only_Where_The_Change_Feed_Is_Published()
    {
        var program = Read("ClinicManagement.API", "Program.cs");

        var registration = program.IndexOf("AddOrUpdate<ClinicManagement.API.BackgroundJobs.RelayWatchJob>", StringComparison.Ordinal);
        Assert.True(registration > 0, "RelayWatchJob is no longer registered in Program.cs.");

        var gate = program.LastIndexOf("if (profile.PublishesChangeFeed)", registration, StringComparison.Ordinal);
        Assert.True(gate > 0, "RelayWatchJob must be registered inside « if (profile.PublishesChangeFeed) ».");
        Assert.DoesNotContain('}', program[program.IndexOf('{', gate)..registration]);
        Assert.Contains("RecurringJob.RemoveIfExists(\"watch-relays\")", program);
    }

    // D27: the change log is the cloud's; a PC has no PC to trim it for.
    [Fact]
    public void The_Change_Log_Pruning_Is_Registered_Only_Where_The_Change_Feed_Is_Published()
    {
        var program = Read("ClinicManagement.API", "Program.cs");

        var registration = program.IndexOf("AddOrUpdate<ClinicManagement.API.BackgroundJobs.PruneClinicChangesJob>", StringComparison.Ordinal);
        Assert.True(registration > 0, "PruneClinicChangesJob is no longer registered in Program.cs.");

        var gate = program.LastIndexOf("if (profile.PublishesChangeFeed)", registration, StringComparison.Ordinal);
        Assert.True(gate > 0, "PruneClinicChangesJob must be registered inside « if (profile.PublishesChangeFeed) ».");
        Assert.DoesNotContain('}', program[program.IndexOf('{', gate)..registration]);
        Assert.Contains("RecurringJob.RemoveIfExists(\"prune-clinic-changes\")", program);
    }

    // A backfill on a copy writes rows the cloud never made; the gate sits after the migrations and before the first one.
    [Fact]
    public void The_Startup_Backfills_Are_Skipped_On_A_Copy()
    {
        var deferred = Read("ClinicManagement.API", "Startup", "DeferredStartupService.cs");

        var migrate = deferred.IndexOf("MigrateAsync(", StringComparison.Ordinal);
        var gate = deferred.IndexOf(".MirrorsCloudClinic", StringComparison.Ordinal);
        var firstBackfill = deferred.IndexOf("SeedAllClinicsAsync(", StringComparison.Ordinal);

        Assert.True(migrate > 0 && gate > 0 && firstBackfill > 0, "A landmark of DeferredStartupService moved.");
        Assert.True(migrate < gate, "The copy must still migrate its schema — the gate belongs after MigrateAsync.");
        Assert.True(gate < firstBackfill, "On a PC de secours the backfills must be skipped — the gate belongs before them.");
    }
}
