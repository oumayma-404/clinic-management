using ClinicManagement.Infrastructure.Security;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Security;

/// <summary>
/// The single implementation of the install's directory-permission policy (security-hardening Part 1,
/// audit § 2 findings 1–3).
///
/// The real ACL change is an <c>icacls</c> invocation, so what is unit-testable — and what actually matters —
/// is the <b>contract with icacls</b>: that the right invocations are issued, in the right order, naming the
/// well-known SIDs rather than localized account names, and that a non-zero exit <b>fails loud</b> instead of
/// leaving the directory readable. Whether NTFS then honours those ACEs is verified on a real Windows box via
/// the operator checklist in <c>packaging/README.md</c> (<c>packaging/</c> is R-1: not CI-runnable).
/// </summary>
public class DirectoryAclHardenerTests : IDisposable
{
    private readonly string _directory;
    private readonly List<IReadOnlyList<string>> _invocations = new();

    public DirectoryAclHardenerTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "acl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup; a leftover temp folder must not fail the suite.
        }
    }

    /// <summary>
    /// Records each invocation and returns success — a hardener over a fake, so no real ACL is touched.
    ///
    /// <para>⚠️ <c>isWindows: () =&gt; true</c> is what lets this class hold on the <b>Linux</b> runner that is
    /// this repository's only automated backend gate. What is asserted below is argument construction — which
    /// SIDs, in which order, with which flags — and none of it touches a Windows API; but <c>Harden</c> reads the
    /// real platform, so on Linux it returned <c>SkippedNotWindows</c> before this fake was ever called and every
    /// assertion indexed an empty list. Ten cases in this class failed that way from the day CI was introduced,
    /// on a security control that passed locally on Windows and was therefore believed green.</para>
    /// </summary>
    private DirectoryAclHardener Recording() => new(
        args =>
        {
            _invocations.Add(args);
            return new AclCommandResult(0, string.Empty);
        },
        isWindows: () => true);

    [Fact]
    public void Harden_issues_grant_then_inheritance_removal_then_users_removal() // [AC-1.1] [AC-2.1]
    {
        var outcome = Recording().Harden(_directory);

        Assert.Equal(AclHardeningOutcome.Applied, outcome);
        Assert.Equal(3, _invocations.Count);

        // 1. Grant first, so access is never lost partway through.
        Assert.Contains("/grant:r", _invocations[0]);
        // 2. Then drop the ACEs inherited from Program Files — this is the Users: Read & Execute.
        Assert.Contains("/inheritance:r", _invocations[1]);
        // 3. Then drop any EXPLICIT Users/Everyone grant, which inheritance removal cannot reach.
        Assert.Contains("/remove:g", _invocations[2]);
    }

    [Fact]
    public void Harden_grants_only_system_administrators_and_network_service() // [AC-2.5]
    {
        Recording().Harden(_directory);

        var grant = _invocations[0];
        Assert.Contains($"{DirectoryAclHardener.SidLocalSystem}:(OI)(CI)F", grant);
        Assert.Contains($"{DirectoryAclHardener.SidAdministrators}:(OI)(CI)F", grant);
        Assert.Contains($"{DirectoryAclHardener.SidNetworkService}:(OI)(CI)F", grant);

        // Nothing is granted to the local Users group or Everyone.
        Assert.DoesNotContain(grant, argument => argument.StartsWith(DirectoryAclHardener.SidUsers, StringComparison.Ordinal));
        Assert.DoesNotContain(grant, argument => argument.StartsWith(DirectoryAclHardener.SidEveryone, StringComparison.Ordinal));
    }

    [Fact]
    public void Harden_removes_users_and_everyone_recursively() // [AC-1.3] [AC-2.1]
    {
        Recording().Harden(_directory);

        var removal = _invocations[2];
        Assert.Contains(DirectoryAclHardener.SidUsers, removal);
        Assert.Contains(DirectoryAclHardener.SidEveryone, removal);
        // /t — the grant initdb needed was inheritable, so its copies exist throughout the tree.
        Assert.Contains("/t", removal);
    }

    // ---- The running account keeps its access (backup-works-everywhere) --------------------------------------

    /// <summary>
    /// The account the process runs as is granted too, so hardening never locks the process out of the directory
    /// it is hardening.
    ///
    /// <para><b>Why this matters more than it looks.</b> Step 2 (<c>/inheritance:r</c>) removes the inherited ACEs
    /// — the point of the whole method — and an inherited ACE is where the running account's access comes from
    /// unless it happens to be one of the three well-known SIDs. Under a de-privileged service account, or an
    /// unelevated one (a developer run: an administrator's SID is present but deny-only), the old three grants
    /// left the process unable to write the directory it had just secured. Two failures followed, both silent:
    /// <c>PgDumpBackupService</c> writes <c>database.dump</c> into that folder immediately afterwards, and its
    /// failure path calls <c>TryDeleteDirectory</c> to remove the partial folder (AC-14.4) — which was refused for
    /// the same reason, leaving an unreadable, undeletable <c>clinic-backup-*</c> folder behind with only a logged
    /// warning. Three of those were found sitting in a real destination, where they also consumed
    /// <c>PruneOldBackupsAsync</c>'s per-pass deletion budget for ever (oldest-first), so retention had silently
    /// stopped pruning anything.</para>
    ///
    /// <para>Asserted against <see cref="DirectoryAclHardener.ComposeGrantArguments"/> rather than through
    /// <c>Harden</c>, so it holds on every platform — see that method's own note.</para>
    /// </summary>
    [Fact]
    public void The_account_the_process_runs_as_is_granted_so_hardening_cannot_lock_it_out()
    {
        // A perfectly ordinary unelevated user SID — the developer-run and de-privileged-service case.
        const string serviceAccount = "S-1-5-21-1004336348-1177238915-682003330-1001";

        var grants = DirectoryAclHardener.ComposeGrantArguments(serviceAccount);

        Assert.Contains($"*{serviceAccount}:(OI)(CI)F", grants);
        // Inheritable, exactly like the three well-known grants: the dump and the file copy write into
        // subdirectories of this folder, so a non-inheritable ACE would fail one step later instead.
        Assert.Equal(4, grants.Count(argument => argument == "/grant:r"));
    }

    /// <summary>
    /// In the packaged install the process <b>is</b> <c>LocalSystem</c>, which is already granted — so no fourth
    /// ACE is added and the posture is byte-identical to what shipped. This is the case that makes the fix above
    /// safe to land, and it cannot be reached through <c>Harden</c> without actually running as <c>LocalSystem</c>.
    /// </summary>
    [Theory]
    [InlineData("S-1-5-18")] // LocalSystem — the API and PostgreSQL services' default account
    [InlineData("S-1-5-20")] // NetworkService
    [InlineData("S-1-5-32-544")] // Administrators
    public void A_process_already_running_as_a_granted_account_adds_no_further_ace(string wellKnownSid)
    {
        var grants = DirectoryAclHardener.ComposeGrantArguments(wellKnownSid);

        Assert.Equal(3, grants.Count(argument => argument == "/grant:r"));
        Assert.Equal(DirectoryAclHardener.ComposeGrantArguments(null), grants);
    }

    /// <summary>
    /// An unreadable identity degrades to the three well-known grants — the posture that shipped — rather than
    /// failing a backup over a diagnostic detail.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unreadable_identity_falls_back_to_the_three_well_known_grants(string? sid)
    {
        var grants = DirectoryAclHardener.ComposeGrantArguments(sid);

        Assert.Equal(3, grants.Count(argument => argument == "/grant:r"));
        Assert.Contains($"{DirectoryAclHardener.SidLocalSystem}:(OI)(CI)F", grants);
        Assert.Contains($"{DirectoryAclHardener.SidAdministrators}:(OI)(CI)F", grants);
        Assert.Contains($"{DirectoryAclHardener.SidNetworkService}:(OI)(CI)F", grants);
    }

    /// <summary>
    /// The widening this fix does <b>not</b> do: whatever the running identity is, neither <c>Users</c> nor
    /// <c>Everyone</c> is ever granted. That is the policy this class exists for, and the one an extra grant
    /// could have quietly undone.
    /// </summary>
    [Theory]
    [InlineData("S-1-5-32-545")] // Users, handed in as if it were the process identity
    [InlineData("S-1-1-0")] // Everyone
    public void Neither_users_nor_everyone_is_ever_granted_whatever_the_process_runs_as(string forbiddenSid)
    {
        var grants = DirectoryAclHardener.ComposeGrantArguments(forbiddenSid);

        Assert.DoesNotContain(grants, argument =>
            argument.StartsWith($"*{forbiddenSid}:", StringComparison.Ordinal));
    }

    [Fact]
    public void Harden_uses_well_known_sids_not_localized_account_names() // French Windows has BUILTIN\Utilisateurs
    {
        Recording().Harden(_directory);

        // Skip element 0 of each invocation — that is the target directory, and the temp path itself lives
        // under C:\Users\..., which would match the name filter for reasons that have nothing to do with ACLs.
        var aclArguments = _invocations.SelectMany(invocation => invocation.Skip(1)).ToList();

        Assert.DoesNotContain(aclArguments, argument => argument.Contains("Users", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(aclArguments, argument => argument.Contains("Everyone", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(aclArguments, argument => argument.Contains("Administrators", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Harden_targets_the_requested_directory_in_every_invocation()
    {
        Recording().Harden(_directory);

        Assert.All(_invocations, invocation => Assert.Equal(_directory, invocation[0]));
    }

    [Theory]
    [InlineData(0)] // grant
    [InlineData(1)] // inheritance removal
    [InlineData(2)] // Users/Everyone removal
    public void Harden_fails_loud_when_any_step_fails(int failingStep) // [AC-1.4] [AC-2.9]
    {
        var step = 0;
        var hardener = new DirectoryAclHardener(
            _ => step++ == failingStep
                ? new AclCommandResult(5, "Accès refusé.")
                : new AclCommandResult(0, string.Empty),
            isWindows: () => true);

        var error = Assert.Throws<InvalidOperationException>(() => hardener.Harden(_directory));

        // The operator must see WHICH step failed and why — not a bare exit code, and never a silent pass.
        Assert.Contains(_directory, error.Message);
        Assert.Contains("Accès refusé.", error.Message);
        Assert.Contains("5", error.Message);
    }

    [Fact]
    public void Harden_stops_at_the_first_failing_step() // never continue against a half-applied ACL
    {
        var attempts = 0;
        var hardener = new DirectoryAclHardener(
            _ =>
            {
                attempts++;
                return new AclCommandResult(1, "boom");
            },
            isWindows: () => true);

        Assert.Throws<InvalidOperationException>(() => hardener.Harden(_directory));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Harden_rejects_a_missing_directory()
    {
        var hardener = Recording();
        var missing = Path.Combine(_directory, "does-not-exist");

        Assert.Throws<DirectoryNotFoundException>(() => hardener.Harden(missing));
        Assert.Empty(_invocations);
    }

    [Fact]
    public void Harden_rejects_an_empty_path()
    {
        Assert.Throws<ArgumentException>(() => Recording().Harden("  "));
    }

    /// <summary>
    /// The other side of the seam: where there are no NTFS ACLs, <c>Harden</c> does nothing, <b>says</b> it did
    /// nothing, and never shells out — and <c>Describe</c> explains itself instead of running <c>icacls</c>.
    ///
    /// <para>⚠️ <b>This case exists because the fix that made this class run on Linux also removed the only thing
    /// exercising the non-Windows path.</b> It used to be covered by accident — every case here failed on the
    /// runner, which is a very expensive way to assert « it skips ». With <c>isWindows: () =&gt; true</c> now
    /// forced above, nothing else in the suite would notice if <c>SkippedNotWindows</c> stopped being returned,
    /// and the next author to meet a platform failure could "fix" it by making production run <c>icacls</c>
    /// everywhere — which on a Linux container means a hard failure on a tool that does not exist.</para>
    /// </summary>
    [Fact]
    public void On_A_Platform_Without_Acls_Harden_Skips_Silently_And_Runs_Nothing()
    {
        var invoked = false;
        var hardener = new DirectoryAclHardener(
            _ =>
            {
                invoked = true;
                return new AclCommandResult(0, string.Empty);
            },
            isWindows: () => false);

        Assert.Equal(AclHardeningOutcome.SkippedNotWindows, hardener.Harden(_directory));
        Assert.False(invoked, "icacls must not be reached on a platform that has no ACLs to set.");
        Assert.Contains("non applicable", hardener.Describe(_directory), StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_never_throws_when_icacls_fails() // diagnostic output, not a gate
    {
        var hardener = new DirectoryAclHardener(
            _ => throw new InvalidOperationException("icacls missing"),
            isWindows: () => true);

        var description = hardener.Describe(_directory);

        Assert.Contains("icacls missing", description);
    }
}
