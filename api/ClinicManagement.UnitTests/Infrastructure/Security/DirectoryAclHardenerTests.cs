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

    /// <summary>Records each invocation and returns success.</summary>
    private DirectoryAclHardener Recording() => new(args =>
    {
        _invocations.Add(args);
        return new AclCommandResult(0, string.Empty);
    });

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
        var hardener = new DirectoryAclHardener(_ =>
            step++ == failingStep
                ? new AclCommandResult(5, "Accès refusé.")
                : new AclCommandResult(0, string.Empty));

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
        var hardener = new DirectoryAclHardener(_ =>
        {
            attempts++;
            return new AclCommandResult(1, "boom");
        });

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

    [Fact]
    public void Describe_never_throws_when_icacls_fails() // diagnostic output, not a gate
    {
        var hardener = new DirectoryAclHardener(_ => throw new InvalidOperationException("icacls missing"));

        var description = hardener.Describe(_directory);

        Assert.Contains("icacls missing", description);
    }
}
