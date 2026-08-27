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

    /// <summary>
    /// The three SIDs <see cref="Harden"/> always grants, in bare form (no <c>*</c> prefix) so they can be
    /// compared against a resolved identity. A process already running as one of them needs no extra grant.
    /// </summary>
    private static readonly string[] WellKnownGrantedSids =
    {
        SidLocalSystem.TrimStart('*'),
        SidNetworkService.TrimStart('*'),
        SidAdministrators.TrimStart('*')
    };

    /// <summary>
    /// The two SIDs this class exists to <b>remove</b>, in bare form. Never granted, whatever is handed in.
    /// </summary>
    private static readonly string[] NeverGrantedSids =
    {
        SidUsers.TrimStart('*'),
        SidEveryone.TrimStart('*')
    };

    private readonly Func<IReadOnlyList<string>, AclCommandResult> _runIcacls;
    private readonly Func<string?> _currentIdentitySid;
    private readonly Func<bool> _isWindows;

    /// <summary>Production constructor — runs the real <c>icacls</c>.</summary>
    public DirectoryAclHardener() : this(RunIcacls)
    {
    }

    /// <summary>
    /// Test seam: supply a fake runner to assert the invocations issued, and optionally a fake identity so the
    /// grant list does not depend on whichever account happens to be running the suite. Production resolves the
    /// identity through <see cref="CurrentIdentitySid"/>.
    /// </summary>
    /// <param name="isWindows">
    /// Whether this platform gets an ACL at all. Defaults to <see cref="OperatingSystem.IsWindows"/>, so
    /// production behaviour is unchanged.
    ///
    /// <para><b>Why this is a seam and not a bare <c>OperatingSystem.IsWindows()</c> call.</b> The policy below
    /// is <i>argument construction</i> — which SIDs, in which order, with which flags — and that is ordinary
    /// platform-independent string work, verified against a fake <paramref name="runIcacls"/> that never touches
    /// a real ACL. Reading the real OS instead made the whole of it unverifiable anywhere but a Windows
    /// developer machine: on the Linux runner that <b>is</b> this repository's only automated backend gate,
    /// <see cref="Harden"/> returned <see cref="AclHardeningOutcome.SkippedNotWindows"/> before the fake was ever
    /// called, so every assertion indexed an empty list. The guard was right and shipped; it simply had no seam,
    /// which is how a security control ends up asserted by nothing that runs.</para>
    ///
    /// <para>⚠️ <see cref="CurrentIdentitySid"/> deliberately keeps its own <c>OperatingSystem.IsWindows()</c>
    /// check and is NOT routed through this. That one guards <c>WindowsIdentity.GetCurrent()</c> — a genuinely
    /// Windows-only .NET API — and the literal call is what proves it safe to the platform-compatibility
    /// analyzer (CA1416). Replacing it with a delegate would silence the analyzer rather than satisfy it. The
    /// asymmetry is the point: seam the policy, never the platform API.</para>
    /// </param>
    public DirectoryAclHardener(
        Func<IReadOnlyList<string>, AclCommandResult> runIcacls,
        Func<string?>? currentIdentitySid = null,
        Func<bool>? isWindows = null)
    {
        ArgumentNullException.ThrowIfNull(runIcacls);
        _runIcacls = runIcacls;
        _currentIdentitySid = currentIdentitySid ?? CurrentIdentitySid;
        _isWindows = isWindows ?? OperatingSystem.IsWindows;
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

        if (!_isWindows())
        {
            return AclHardeningOutcome.SkippedNotWindows;
        }

        // `/grant:r` replaces any previous grant for the same SID rather than adding a second ACE, so
        // re-running this (a reinstall) converges instead of accumulating.
        //
        // ⚠️ The account THIS PROCESS runs as is granted too, and that is load-bearing rather than defensive.
        // Step 2 below removes the inherited ACEs — which is the whole point, it is what drops the
        // `Users: Read & Execute` inherited from `{autopf}` — but an inherited ACE is also where the running
        // account's own access comes from unless it happens to be LocalSystem, NetworkService or an *elevated*
        // administrator. Under a de-privileged service account, or an unelevated one (a developer run, where an
        // administrator's SID is present but deny-only), the three grants above leave the process locked out of
        // the directory it is hardening. Two things then break, in this order:
        //
        //   (a) the backup writes `database.dump` into that folder immediately afterwards, so a *successful*
        //       hardening breaks the very operation it is protecting; and
        //   (b) when a later step fails, `PgDumpBackupService`'s catch calls `TryDeleteDirectory` to remove the
        //       partial folder (AC-14.4) — and that is refused for the same reason, so the half-written backup
        //       survives, unreadable and undeletable, with only a logged warning behind it.
        //
        // (b) is not hypothetical: it is how three orphaned `clinic-backup-*` folders came to sit permanently in
        // a destination, where they also consume `PruneOldBackupsAsync`'s per-pass deletion budget for ever —
        // oldest-first, so retention stops pruning anything real and the destination grows without bound.
        //
        // This widens nothing the policy cares about: the policy excludes `Users` and `Everyone`, and where the
        // process already runs as one of the three SIDs above this grant is a no-op.
        Execute(
            "l'attribution des droits au service",
            directory,
            ComposeGrantArguments(_currentIdentitySid()).ToArray());

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
        if (!_isWindows())
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

    /// <summary>
    /// The <c>/grant:r</c> arguments for step 1: the three well-known SIDs, plus
    /// <paramref name="currentIdentitySid"/> when it is neither null nor already one of them.
    ///
    /// <para>Extracted so the rule is assertable <b>on any platform</b>. <see cref="Harden"/> returns
    /// <see cref="AclHardeningOutcome.SkippedNotWindows"/> before it ever composes these, so a test that went
    /// through <c>Harden</c> could only exercise this on Windows — and the case that matters most (the process
    /// already running as <c>LocalSystem</c>, i.e. the packaged install) is unreachable there anyway without
    /// actually being <c>LocalSystem</c>.</para>
    ///
    /// <para><c>public</c> rather than <c>internal</c> because this solution has no <c>InternalsVisibleTo</c> —
    /// the alternative is reaching in by reflection, which <c>CnamVlcTests</c> documents as the workaround it is.
    /// « What does hardening grant? » is a fair question to ask this class anyway.</para>
    /// </summary>
    public static IReadOnlyList<string> ComposeGrantArguments(string? currentIdentitySid)
    {
        var grants = new List<string>
        {
            "/grant:r", $"{SidLocalSystem}:(OI)(CI)F",
            "/grant:r", $"{SidAdministrators}:(OI)(CI)F",
            "/grant:r", $"{SidNetworkService}:(OI)(CI)F"
        };

        // ⚠️ `Users`/`Everyone` are excluded explicitly, so « this method never grants either » is true by
        // construction rather than by the observation that `WindowsIdentity.GetCurrent().User` returns an account
        // and those two are groups. Step 3 removes them a few lines later; granting one here would have this
        // method quietly undo its own policy, and that is too load-bearing to rest on an argument about which
        // SIDs a token can hold.
        if (!string.IsNullOrWhiteSpace(currentIdentitySid)
            && !WellKnownGrantedSids.Contains(currentIdentitySid)
            && !NeverGrantedSids.Contains(currentIdentitySid))
        {
            grants.Add("/grant:r");
            grants.Add($"*{currentIdentitySid}:(OI)(CI)F");
        }

        return grants;
    }

    /// <summary>
    /// The SID of the account this process runs as, or <c>null</c> when it cannot be read.
    ///
    /// <para>A <b>SID and not a name</b>, for this file's stated reason: account names are localized, so a
    /// name-based ACE would silently no-op on exactly the French Windows installs this ships to. Null on any
    /// failure — the three well-known grants still apply, which is the behaviour that shipped before, so an
    /// unreadable identity degrades to the old posture rather than failing a backup.</para>
    /// </summary>
    private static string? CurrentIdentitySid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return identity.User?.Value;
        }
        catch
        {
            return null;
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
