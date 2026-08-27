using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// The projection the EF global query filter reads (multi-tenant-cloud US-2). Three scope states must arrive at
/// the filter as two synchronous values, and the mapping of the middle one is the whole security change:
/// <c>Unset</c> must present as « not system-wide, no clinic », which is what makes the filter refuse instead of
/// switching itself off.
///
/// <para>It composes the <b>real</b> <see cref="TenantScope"/> rather than a mock, deliberately: a mocked scope
/// would let the provider agree with a stubbed answer while the two disagreed in production, and the chain
/// scope → provider → filter is exactly one link long.</para>
/// </summary>
public class CurrentClinicProviderTests
{
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static (ITenantScope Scope, ICurrentClinicProvider Provider) Build()
    {
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);
        return (scope, new CurrentClinicProvider(scope));
    }

    // [US-2] The refusal state. Before this feature these same two readings meant "filter inactive".
    [Fact]
    public void An_Unset_Scope_Presents_As_Neither_System_Wide_Nor_A_Clinic()
    {
        var (_, provider) = Build();

        Assert.False(provider.IsSystemWide);
        Assert.Null(provider.ClinicId);
    }

    [Fact]
    public void A_Clinic_Scope_Presents_That_Clinic() // [US-2]
    {
        var (scope, provider) = Build();
        scope.UseClinic(ClinicA);

        Assert.False(provider.IsSystemWide);
        Assert.Equal(ClinicA, provider.ClinicId);
    }

    // [US-2] SystemWide is the ONLY state that returns every row — and it carries no clinic, so a caller that
    // declared itself cross-clinic cannot also be read as scoped to the empty clinic.
    [Fact]
    public void A_System_Wide_Scope_Presents_As_System_Wide_With_No_Clinic()
    {
        var (scope, provider) = Build();
        scope.UseSystemWide("a test");

        Assert.True(provider.IsSystemWide);
        Assert.Null(provider.ClinicId);
    }
}
