using System.Reflection;
using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Relay;
using ClinicManagement.UnitTests.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The two halves of D15 (<c>clinic-pc-copy</c>) that no single test can see, derived from the code:
/// <list type="number">
///   <item>every command a door the fenced cloud keeps open (FR-11) can send reaches only carried tables the net lets
///   through — or the net refuses the account change the door exists for;</item>
///   <item>every path that iterates every cabinet with no request behind it asks <see cref="IClinicWriteFence"/> before
///   it acts — or one fenced cabinet fails the whole run (and the reminder outbox resends every minute).</item>
/// </list>
/// </summary>
public class RelayFenceCoverageTests
{
    /// <summary>A carried table an FR-11 command's handler can reach without writing it for a fenced cabinet, and why.</summary>
    private static readonly Dictionary<string, string> ReviewedFr11Reads = new(StringComparer.Ordinal)
    {
        ["JoinClinicCommand:Clinic"] = "reads the cabinet its clinic code names; the account it creates is a User",
        ["EnrolTotpCommand:Clinic"] = "reads the cabinet's name for the authenticator's label",
        ["PairRelayCommand:Clinic"] = "reads the cabinet's name for the PC's pairing answer",
        ["VerifyClinicSignUpCommand:Clinic"] = "creates a NEW cabinet, which has no PC de secours to be fenced by",
        ["VerifyClinicSignUpCommand:ProcedureType"] = "seeds a NEW cabinet's catalogue; no PC de secours yet",
        ["CreateClinicCommand:Clinic"] = "the first-run setup creates an install's first cabinet; no PC de secours yet",
        ["CreateClinicCommand:ProcedureType"] = "seeds that new cabinet's catalogue; no PC de secours yet",
    };

    /// <summary>All-cabinet paths that never write a carried table the net refuses, each with its reason.</summary>
    private static readonly Dictionary<string, string> ReviewedAllCabinetPaths = new(StringComparer.Ordinal)
    {
        ["BackupJob.cs"] = "a LAN server's own dump (BacksUpItsOwnData); BackupRun is not carried",
        ["ClinicActivityCounterJob.cs"] = "the vendor's counters (ClinicActivityDay/Snapshot), never carried",
        ["ClinicRecoveryPointJob.cs"] = "recovery points live in the cloud's object store and its own ledger, never carried",
        ["MessagingAllowanceJob.cs"] = "the vendor's forfait tables, never carried, and bell rows (FR-11 set)",
        ["PushDispatchJob.cs"] = "the push outbox, never carried",
        ["RelayWatchJob.cs"] = "the relay's own incidents and the admins' bell rows (FR-11 set)",
        ["SessionFamilyPurgeJob.cs"] = "sessions are each install's own, never carried",
        ["StockExpiryJob.cs"] = "bell rows only, which the fenced cloud keeps writing (FR-11 set)",
        ["SubscriptionWarningJob.cs"] = "bell rows only, which the fenced cloud keeps writing (FR-11 set)",
    };

    private static string FileNamed(string name) =>
        SolutionSources.CsFiles(SolutionSources.Root())
            .Where(p => !p.Contains("ClinicManagement.UnitTests", StringComparison.Ordinal))
            .Single(p => string.Equals(Path.GetFileName(p), name, StringComparison.Ordinal));

    private static readonly Regex NewCommand = new(@"new\s+(\w+Command)\s*[\(\{]", RegexOptions.Compiled);

    private static IReadOnlySet<string> CarriedTables()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=none;Password=none").Options);
        var plan = ClinicRelayScope.For(db.Model);
        return db.Model.GetEntityTypes().Select(t => t.ClrType)
            .Where(t => plan.Find(t) is not null)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<Type> Fr11Controllers() =>
        typeof(ClinicManagement.API.Controllers.AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract
                        && t.GetCustomAttribute<AllowedWhileCloudFencedAttribute>(inherit: true) is not null);

    /// <summary>The commands a controller sends: bound from the request, or built in the action.</summary>
    private static IEnumerable<Type> CommandsOf(Type controller)
    {
        var application = typeof(IClinicWriteFence).Assembly;
        var bound = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType))
            .Where(t => typeof(IBaseRequest).IsAssignableFrom(t));
        var source = File.ReadAllText(FileNamed(controller.Name + ".cs"));
        var built = NewCommand.Matches(source).Select(m => m.Groups[1].Value).Distinct()
            .Select(name => application.GetTypes().SingleOrDefault(t => t.Name == name))
            .Where(t => t is not null && typeof(IBaseRequest).IsAssignableFrom(t))
            .Cast<Type>();
        return bound.Concat(built).Distinct();
    }

    /// <summary>The tables a handler can reach: one per injected repository, plus the bell for the feed's writer.</summary>
    private static IEnumerable<string> TablesReachedBy(Type command)
    {
        var handler = typeof(IClinicWriteFence).Assembly.GetTypes().SingleOrDefault(t => t.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) && i.GetGenericArguments()[0] == command));
        if (handler is null)
        {
            return Array.Empty<string>();
        }

        return handler.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType.Name)
            .Select(name => name == nameof(INotificationGenerator)
                ? "StaffNotification"
                : Regex.Match(name, "^I(\\w+)Repository$") is { Success: true } m ? m.Groups[1].Value : null)
            .Where(n => n is not null)
            .Cast<string>()
            .Distinct();
    }

    [Fact]
    public void Every_Door_The_Fenced_Cloud_Keeps_Open_Reaches_Only_What_The_Net_Lets_Through()
    {
        var carried = CarriedTables();
        var commands = Fr11Controllers().SelectMany(CommandsOf).Distinct().ToList();

        Assert.True(commands.Count >= 15, $"Only {commands.Count} FR-11 commands found — the scan no longer reaches them.");

        var offenders = commands
            .SelectMany(c => TablesReachedBy(c).Select(t => (Key: $"{c.Name}:{t}", Table: t)))
            .Where(x => carried.Contains(x.Table) && !RelayFence.AllowedOnFencedCloud.ContainsKey(x.Table)
                        && !ReviewedFr11Reads.ContainsKey(x.Key))
            .Select(x => x.Key)
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "An FR-11 door reaches a carried table the net refuses on a fenced cloud — add the table to "
            + "RelayFence.AllowedOnFencedCloud (with the merge it needs at the return) or review the read: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_Reviewed_Fr11_Read_Is_Still_Reached()
    {
        var commands = Fr11Controllers().SelectMany(CommandsOf).Distinct().ToList();
        var reached = commands.SelectMany(c => TablesReachedBy(c).Select(t => $"{c.Name}:{t}")).ToHashSet();

        foreach (var key in ReviewedFr11Reads.Keys)
        {
            Assert.True(reached.Contains(key), $"{key} is no longer reached; drop it from the reviewed list.");
        }
    }

    /// <summary>Every source in the jobs and startup folders that declares it iterates every cabinet.</summary>
    private static List<string> AllCabinetPaths()
    {
        var root = SolutionSources.Root().FullName;
        return new[] { "BackgroundJobs", "Startup" }
            .SelectMany(folder => SolutionSources.CsFiles(new DirectoryInfo(Path.Combine(root, "ClinicManagement.API", folder))))
            .Where(path => SolutionSources.WithoutComments(File.ReadAllText(path)).Contains("UseSystemWide(", StringComparison.Ordinal))
            .ToList();
    }

    [Fact]
    public void Every_Path_Over_Every_Cabinet_Asks_The_Fence_First()
    {
        var paths = AllCabinetPaths();
        Assert.True(paths.Count >= 10, $"Only {paths.Count} all-cabinet paths found — the scan no longer reaches them.");

        var offenders = paths
            .Where(path => !ReviewedAllCabinetPaths.ContainsKey(Path.GetFileName(path)))
            .Where(path => !File.ReadAllText(path).Contains(nameof(IClinicWriteFence), StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These iterate every cabinet and never ask IClinicWriteFence — one fenced cabinet would fail the run: "
            + string.Join(", ", offenders));
    }

    // Both directions: a reviewed path that now asks the fence, or is gone, is an exemption nobody needs.
    [Fact]
    public void Every_Reviewed_All_Cabinet_Path_Exists_And_Still_Does_Not_Ask()
    {
        var paths = AllCabinetPaths().ToDictionary(Path.GetFileName, p => p, StringComparer.Ordinal);

        foreach (var (file, reason) in ReviewedAllCabinetPaths)
        {
            Assert.True(paths.TryGetValue(file, out var path), $"{file} ({reason}) is no longer an all-cabinet path.");
            Assert.DoesNotContain(nameof(IClinicWriteFence), File.ReadAllText(path!));
        }
    }

    // The startup backfills run inside the cloud's start: a refused save there would stop the cloud during a cut.
    [Theory]
    [InlineData("ClinicCatalogSeeder.cs")]
    [InlineData("GoogleTokenProtectionBackfill.cs")]
    public void Every_Startup_Backfill_Asks_The_Fence(string file)
    {
        Assert.Contains(nameof(IClinicWriteFence), File.ReadAllText(FileNamed(file)));
    }
}
