using System.Diagnostics;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>Outcome of one <c>icacls</c> invocation.</summary>
public sealed record AclCommandResult(int ExitCode, string Output);

/// <summary>What <see cref="DirectoryAclHardener.Harden"/> actually did.</summary>
public enum AclHardeningOutcome
{
    /// <summary>Permissions were tightened.</summary>
    Applied,

    /// <summary>Not Windows — NTFS ACLs do not apply, so nothing was changed.</summary>
    SkippedNotWindows
}

/// <summary>
/// The <b>single</b> implementation of this install's directory-permission policy: break ACL inheritance,
/// grant only the accounts that need access, and remove any grant to <c>Users</c> / <c>Everyone</c>.
///
/// Audit § 2 findings 1–3: the installer's <c>[Dirs] Permissions:</c> entries only <b>add</b> an ACE and
/// leave the inherited <c>Users: Read &amp; Execute</c> from <c>{autopf}</c> intact, and the Full Control
/// granted to <c>BUILTIN\Users</c> so de-privileged <c>initdb</c> can run was never revoked. The result was
/// that every local account on the clinic PC could read the whole patient database, every uploaded
/// radiograph, the logs, and the entire <c>.local/</c> trust store — the JWT signing key, the HTTPS server
/// key and the Data Protection key ring.
///
/// Two callers share this class so the policy cannot drift between them: the Local <c>harden-permissions</c>
/// console verb (invoked by the server installer) and the one-click backup, whose output folder would
/// otherwise hand out an unprotected copy of everything the install protects.
///
/// <para><b>Mechanism.</b> <c>icacls</c>, matching what <c>clinic-server.iss</c> already did and requiring no
/// new dependency (both projects target <c>net8.0</c>, not <c>net8.0-windows</c>, so the managed
/// <c>System.Security.AccessControl</c> APIs are unavailable). Every invocation's exit code is checked and a
/// failure throws with the command output attached — a permission step that cannot be applied must fail
/// loud, never silently leave the directory readable (spec AC-1.4, AC-2.9).</para>
///
/// <para><b>Well-known SIDs, not names.</b> Account names are localized: a French Windows install has
/// <c>BUILTIN\Utilisateurs</c>, not <c>Users</c>, so a name-based ACL edit would silently no-op on exactly
/// the machines this ships to.</para>
/// </summary>
public sealed class DirectoryAclHardener
{
    /// <summary><c>NT AUTHORITY\SYSTEM</c> — the API and PostgreSQL services' default account.</summary>
    public const string SidLocalSystem = "*S-1-5-18";

    /// <summary><c>NT AUTHORITY\NETWORK SERVICE</c> — in case a service is reconfigured to run under it.</summary>
    public const string SidNetworkService = "*S-1-5-20";

    /// <summary><c>BUILTIN\Administrators</c> — so an operator can still back up and restore.</summary>
    public const string SidAdministrators = "*S-1-5-32-544";

    /// <summary><c>BUILTIN\Users</c> — every local non-admin account. Must never have access.</summary>
    public const string SidUsers = "*S-1-5-32-545";

    /// <summary><c>Everyone</c>. Must never have access.</summary>
    public const string SidEveryone = "*S-1-1-0";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

    private readonly Func<IReadOnlyList<string>, AclCommandResult> _runIcacls;

    /// <summary>Production constructor — runs the real <c>icacls</c>.</summary>
    public DirectoryAclHardener() : this(RunIcacls)
    {
    }

    /// <summary>Test seam: supply a fake runner to assert the invocations issued.</summary>
    public DirectoryAclHardener(Func<IReadOnlyList<string>, AclCommandResult> runIcacls)
    {
        ArgumentNullException.ThrowIfNull(runIcacls);
        _runIcacls = runIcacls;
    }

    /// <summary>
    /// Tightens <paramref name="directory"/> so only <c>LocalSystem</c>, <c>NetworkService</c> and
    /// <c>Administrators</c> retain access.
    ///
    /// The order matters and is deliberate:
    ///   1. <b>grant first</b>, inheritably, so access is never lost partway through;
    ///   2. <b>then</b> remove inherited ACEs — this is what drops the <c>Users: Read &amp; Execute</c>
    ///      inherited from <c>Program Files</c>;
    ///   3. <b>then</b> remove any <i>explicit</i> <c>Users</c>/<c>Everyone</c> grant, recursively — this is
    ///      what drops the Full Control the installer had to give <c>initdb</c>, which is explicit rather
    ///      than inherited and so survives step 2.
    ///
    /// Doing both 2 and 3 means one method handles both shapes of the problem uniformly.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directory"/> does not exist.</exception>
    /// <exception cref="InvalidOperationException">Any <c>icacls</c> step failed.</exception>
    public AclHardeningOutcome Harden(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Directory path must not be null or empty.", nameof(directory));
        }

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Impossible de sécuriser « {directory} » : le dossier n'existe pas.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return AclHardeningOutcome.SkippedNotWindows;
        }

        // `/grant:r` replaces any previous grant for the same SID rather than adding a second ACE, so
        // re-running this (a reinstall) converges instead of accumulating.
        Execute(
            "l'attribution des droits au service",
            directory,
            "/grant:r", $"{SidLocalSystem}:(OI)(CI)F",
            "/grant:r", $"{SidAdministrators}:(OI)(CI)F",
            "/grant:r", $"{SidNetworkService}:(OI)(CI)F");

        Execute("la suppression des droits hérités", directory, "/inheritance:r");

        Execute(
            "la suppression des droits « Utilisateurs »",
            directory,
            "/remove:g", SidUsers,
            "/remove:g", SidEveryone,
            "/t");

        return AclHardeningOutcome.Applied;
    }

    /// <summary>
    /// Returns the current ACL listing for <paramref name="directory"/> so the caller can record the
    /// resulting posture (the installer log, the operator checklist). Never throws on a non-zero exit —
    /// this is diagnostic output, not a gate.
    /// </summary>
    public string Describe(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "(ACL non applicable : système non-Windows)";
        }

        try
        {
            return _runIcacls(new[] { directory }).Output;
        }
        catch (Exception ex)
        {
            return $"(impossible de lire les droits : {ex.Message})";
        }
    }

    /// <summary>Runs one <c>icacls</c> step and fails loud on a non-zero exit.</summary>
    private void Execute(string stepDescription, string directory, params string[] arguments)
    {
        var argv = new List<string>(arguments.Length + 1) { directory };
        argv.AddRange(arguments);

        var result = _runIcacls(argv);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Échec de {stepDescription} sur « {directory} » (icacls, code {result.ExitCode}). " +
                $"Détail : {result.Output}");
        }
    }

    /// <summary>Invokes the real <c>icacls.exe</c>, capturing both streams.</summary>
    private static AclCommandResult RunIcacls(IReadOnlyList<string> arguments)
    {
        // Resolve from the system directory rather than PATH so a shadowed icacls.exe cannot be used.
        var systemIcacls = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "icacls.exe");

        var startInfo = new ProcessStartInfo
        {
            FileName = File.Exists(systemIcacls) ? systemIcacls : "icacls.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Argument LIST, never a concatenated command line — paths contain spaces and must not be re-parsed.
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Impossible de démarrer icacls.exe.");

        // Read both streams concurrently: reading one to completion while the other fills its buffer
        // deadlocks the child process.
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)CommandTimeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone — the timeout message below is what matters.
            }

            throw new InvalidOperationException(
                $"icacls.exe n'a pas terminé dans le délai imparti ({CommandTimeout.TotalMinutes:0} min).");
        }

        // Ensures the redirected streams are fully drained after the timed wait.
        process.WaitForExit();

        var output = string.Join(
            Environment.NewLine,
            new[] { standardOutput.GetAwaiter().GetResult(), standardError.GetAwaiter().GetResult() }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Trim()));

        return new AclCommandResult(process.ExitCode, output);
    }
}
