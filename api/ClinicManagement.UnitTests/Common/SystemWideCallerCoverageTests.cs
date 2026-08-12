using System.Runtime.CompilerServices;
using ClinicManagement.API.BackgroundJobs;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// Every path that reads a clinic-filtered entity with <b>no HTTP context</b> must declare its tenant scope
/// (multi-tenant-cloud US-2, risk R-1). This is the guard for the story's whole failure mode: the query filters
/// now refuse an unset scope, so a job that forgets reads <i>nothing</i> and logs a clean run — reminders stop,
/// reminders stop, « Envoyer par email » stops, and every layer reports success.
///
/// <para><b>The criterion is stated before the enumeration, and the enumeration is derived from it.</b> Reading
/// it off « is it a job? » produced a wrong list in both directions during planning, so the candidates are found
/// by reflection over the API assembly (every background job, every hosted service, every maintenance verb) plus
/// a source scan for <c>CreateScope()</c> — the shape that opens a fresh scope with no scope in it. A
/// <i>new</i> job that forgets fails this test on the day it is written, which a folder list cannot do.</para>
///
/// <para><b>What it cannot see.</b> It matches source text, so a path that names the call in a comment would
/// satisfy it. That is the same trade <c>DeploymentProfileCoverageTests</c> makes, and the alternative — an IL or
/// syntax-tree walk — buys precision against a failure nobody has ever made while adding a dependency this
/// project does not have.</para>
/// </summary>
public class SystemWideCallerCoverageTests
{
    /// <summary>
    /// Candidates that read no clinic-filtered entity, each with the structural reason. Not « we decided it is
    /// fine »: every entry names a mechanism that makes the query filters unreachable from that file.
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ProvisionCertCommand.cs"] = "mints certificates into .local/; never opens a DbContext",
        ["HardenPermissionsCommand.cs"] = "sets filesystem ACLs; never opens a DbContext",
        ["CredentialProtectionCommand.cs"] = "encrypts a string through Data Protection; never opens a DbContext",
        ["RestoreBackupCommand.cs"] = "runs pg_restore and bumps TokenVersion over raw ADO (NpgsqlCommand) — there "
                                      + "is no DbContext and therefore no query filter to satisfy",
        ["AuditSaveChangesInterceptor.cs"] = "its child scope writes only AuditEntry, which carries no query "
                                            + "filter by design (nullable ClinicId)",
        // ⚠️ The one exemption whose reason is « something else declares it », which is normally the excuse this
        // guard exists to reject. It holds here because the verb's entire body is one call to
        // ClinicActivityCounterJob.CountClinicActivity(), whose FIRST statement is UseSystemWide — and ITenantScope
        // is single-assignment, so declaring it in the verb as well would throw. Reusing the job rather than
        // copying the counter rules is the point of the verb; a second declaration would be the price of a second
        // implementation.
        ["CountActivityCommand.cs"] = "delegates wholly to ClinicActivityCounterJob, which declares UseSystemWide "
                                      + "as its first act; ITenantScope is single-assignment so a second "
                                      + "declaration here would throw"
    };

    /// <summary>
    /// Both calls count. <c>UseClinic</c> is the right answer for a single-clinic path — <c>PdfGenerationJob</c>
    /// renders one document, the Google dispatcher pushes one appointment — and demanding SystemWide everywhere
    /// would push the widest scope in the design onto the narrowest work.
    /// </summary>
    private static bool DeclaresAScope(string source) =>
        source.Contains("UseSystemWide(", StringComparison.Ordinal)
        || source.Contains("UseClinic(", StringComparison.Ordinal);

    /// <summary>
    /// Production sources only. The test project mocks <c>IServiceScopeFactory.CreateScope()</c> in six places
    /// and none of them is a read path.
    /// </summary>
    private static IEnumerable<string> ProductionSources(DirectoryInfo root) =>
        SolutionSources.CsFiles(root)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}ClinicManagement.UnitTests{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The types that run with no HTTP context, by reflection rather than by folder: every background job, every
    /// <see cref="IHostedService"/>, and every console verb.
    /// </summary>
    private static IEnumerable<Type> NoHttpContextTypes()
    {
        var api = typeof(NotificationJob).Assembly;

        return api.GetTypes()
            // ⚠️ `IsAbstract: false` alone silently excluded EVERY console verb: a `static class` is abstract AND
            // sealed in metadata, so the Maintenance branch below matched nothing for this guard's whole life and
            // the verbs were covered only incidentally, by the CreateScope() source scan. Found by
            // `clinic-subscription` Part F writing the same filter and getting an empty set.
            .Where(t => t is { IsClass: true } && (!t.IsAbstract || t.IsSealed))
            // Async state machines and lambda closures are nested classes of the job that declares them, and in a
            // Debug build they are classes rather than structs — so without this they arrive as candidates whose
            // "source file" is `<FlagExpiringStock>d__3.cs`.
            .Where(t => !t.IsNested && !Attribute.IsDefined(t, typeof(CompilerGeneratedAttribute)))
            .Where(t =>
                t.Namespace == typeof(NotificationJob).Namespace
                || typeof(IHostedService).IsAssignableFrom(t)
                || (t.Namespace == "ClinicManagement.API.Maintenance" && t.Name.EndsWith("Command", StringComparison.Ordinal)))
            .Distinct();
    }

    private static string SourceOf(Type type, IReadOnlyCollection<string> sources)
    {
        var expected = type.Name + ".cs";
        var path = sources.SingleOrDefault(p => string.Equals(Path.GetFileName(p), expected, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(path); // A type this guard cannot locate is a hole, not a pass.
        return path!;
    }

    // [US-2][R-1] The guard itself: no candidate may be silent about the rows it is allowed to read.
    [Fact]
    public void Every_Path_Without_An_Http_Context_Declares_Its_Tenant_Scope()
    {
        var root = SolutionSources.Root();
        var sources = ProductionSources(root).ToList();

        var candidates = NoHttpContextTypes()
            .Select(t => SourceOf(t, sources))
            .Concat(sources.Where(p => File.ReadAllText(p).Contains("CreateScope()", StringComparison.Ordinal)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.NotEmpty(candidates);

        var offenders = candidates
            .Where(path => !Exempt.ContainsKey(Path.GetFileName(path)))
            .Where(path => !DeclaresAScope(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root.FullName, path))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These paths run with no HTTP context and never declare a tenant scope:{Environment.NewLine}"
            + string.Join(Environment.NewLine, offenders.Select(o => "  " + o))
            + $"{Environment.NewLine}The clinic query filters refuse an unset scope, so each of these reads "
            + "nothing and reports success. Call UseSystemWide(reason) if it genuinely iterates every clinic, "
            + "UseClinic(id) if it handles one — or add it to Exempt with the structural reason it touches no "
            + "filtered entity.");
    }

    [Fact]
    public void Every_Exemption_Still_Names_A_File_That_Exists() // [US-2]
    {
        var present = ProductionSources(SolutionSources.Root())
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (file, reason) in Exempt)
        {
            Assert.True(present.Contains(file), $"Exempted file '{file}' ({reason}) no longer exists.");
        }
    }

    /// <summary>
    /// The paths that <b>have</b> an HTTP context and still must declare a scope, so the criterion above cannot
    /// reach them. One entry today: an <c>[AllowAnonymous]</c> action has no <c>User</c> row for
    /// <c>TenantScopeMiddleware</c> to resolve, so its scope lands <c>Unset</c> — where a read of a filtered entity
    /// returns nothing and the endpoint reports success.
    ///
    /// <para>⚠️ Hand-named, unlike everything else in this class, and it is the one place that is unavoidable:
    /// deriving « an anonymous action that reads a filtered entity » needs a syntax-tree walk this project has no
    /// dependency for. It is a list of one, and the sibling test asserts the file still exists so a rename cannot
    /// leave it silently checking nothing.</para>
    /// </summary>
    private static readonly Dictionary<string, string> ScopedDespiteHavingAnHttpContext =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["MetaWebhookController.cs"] = "anonymous, so TenantScopeMiddleware leaves the scope Unset; it resolves "
                                           + "the cabinet through the filtered ClinicReminderSettings table",
        };

    // [US-2] + vendor-whatsapp-messaging-quota FR-7a. The criterion above is « no HTTP context »; these have one.
    [Fact]
    public void Every_Anonymous_Writer_Of_A_Filtered_Entity_Declares_Its_Tenant_Scope()
    {
        var root = SolutionSources.Root();
        var sources = ProductionSources(root).ToList();

        foreach (var (file, reason) in ScopedDespiteHavingAnHttpContext)
        {
            var path = sources.SingleOrDefault(
                p => string.Equals(Path.GetFileName(p), file, StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(path); // A renamed file must fail here rather than pass by not being found.
            Assert.True(
                DeclaresAScope(File.ReadAllText(path!)),
                $"'{file}' ({reason}) declares no tenant scope. It would verify its payload, resolve no cabinet, "
                + "write nothing and answer 200.");
        }
    }

    // [US-2] A derived guard that has never gone red is not yet a guard. Rather than asking a reviewer to delete
    // a call by hand, this feeds the predicate the real StockExpiryJob source with its declaration stripped and
    // asserts the verdict flips — so the thing being proved is the exact function the assertion above uses.
    [Fact]
    public void The_Guard_Rejects_A_Job_Whose_Declaration_Is_Removed()
    {
        var root = SolutionSources.Root();
        var job = File.ReadAllText(SourceOf(typeof(StockExpiryJob), ProductionSources(root).ToList()));

        Assert.True(DeclaresAScope(job));

        var stripped = job.Replace("UseSystemWide(", "NoLongerDeclaring(", StringComparison.Ordinal);

        Assert.NotEqual(job, stripped);
        Assert.False(DeclaresAScope(stripped));
    }
}
