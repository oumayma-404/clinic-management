using ClinicManagement.API.Hubs;
using ClinicManagement.Domain.Repositories;
using Xunit;

namespace ClinicManagement.UnitTests.Hubs;

/// <summary>
/// The hub's blind spot, pinned (multi-tenant-cloud US-2, step 10).
///
/// <para><b>HTTP middleware does not run per hub invocation.</b> <c>TenantScopeMiddleware</c> sets the scope on
/// the negotiate request, but a hub method executes in its own DI scope — which lands in
/// <c>TenantScopeKind.Unset</c>, and the global query filters now <b>refuse</b> that. A hub method touching a
/// clinic-filtered entity would therefore read nothing, silently, with the realtime client's bare
/// <c>catch {}</c> swallowing any sign of it.</para>
///
/// <para><c>ClinicHub</c> is safe today only because it reads exactly one thing — <c>User</c>, which carries no
/// filter. That is a property of its dependencies, so this asserts on its dependencies: the day someone injects a
/// filtered repository here, this fails and the note on the hub gets read. A behavioural test could not catch it,
/// because the broken version <i>returns successfully</i> with an empty result.</para>
/// </summary>
public class ClinicHubTenantScopeTests
{
    /// <summary>
    /// The only repository the hub may depend on. <c>User</c> is unfiltered by design (the auth and join flows
    /// resolve it before any clinic is in scope), so resolving a clinic from it needs no tenant scope.
    /// </summary>
    private static readonly HashSet<Type> UnfilteredDependencies = new() { typeof(IUserRepository) };

    // [US-2] Derived from the constructor, not from a remembered list of what the hub currently does.
    [Fact]
    public void The_Hub_Depends_Only_On_Unfiltered_Data()
    {
        var constructor = Assert.Single(typeof(ClinicHub).GetConstructors());

        var unexpected = constructor.GetParameters()
            .Select(p => p.ParameterType)
            .Where(t => !UnfilteredDependencies.Contains(t))
            .ToList();

        Assert.True(
            unexpected.Count == 0,
            "ClinicHub gained a dependency beyond IUserRepository: "
            + string.Join(", ", unexpected.Select(t => t.Name))
            + ". A hub method runs with NO tenant scope (HTTP middleware does not run per invocation), so a "
            + "clinic-filtered read here returns nothing and reports success. Set the scope explicitly from the "
            + "clinic the hub already resolves, then add the dependency to UnfilteredDependencies with the reason.");
    }
}
