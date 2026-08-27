using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// The scope's own semantics (multi-tenant-cloud US-2). The three states are the easy half; the assertions that
/// matter are the two <b>refusals</b>, because they are what the interface leaves open and what the plan settled:
/// a scope may not be widened after it has been narrowed, nor narrowed after it has been widened. A silent
/// widening is how a single-clinic path becomes a cross-clinic one with nobody noticing, and a silent narrowing
/// inside an iterating job would hide the fact that it needs a child scope.
/// </summary>
public class TenantScopeTests
{
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static TenantScope Scope() => new(NullLogger<TenantScope>.Instance);

    [Fact]
    public void A_Fresh_Scope_Is_Unset() // [US-2]
    {
        var scope = Scope();

        Assert.Equal(TenantScopeKind.Unset, scope.Kind);
        Assert.Null(scope.ClinicId);
        Assert.Null(scope.SystemWideReason);
    }

    [Fact]
    public void UseClinic_Sets_The_Clinic() // [US-2]
    {
        var scope = Scope();
        scope.UseClinic(ClinicA);

        Assert.Equal(TenantScopeKind.Clinic, scope.Kind);
        Assert.Equal(ClinicA, scope.ClinicId);
    }

    // [US-2] The reason is recorded, not merely required — it is the answer to « who read across clinics, and why ».
    [Fact]
    public void UseSystemWide_Records_Its_Reason_And_Carries_No_Clinic()
    {
        var scope = Scope();
        scope.UseSystemWide("NotificationJob dispatches every clinic's reminders");

        Assert.Equal(TenantScopeKind.SystemWide, scope.Kind);
        Assert.Null(scope.ClinicId);
        Assert.Equal("NotificationJob dispatches every clinic's reminders", scope.SystemWideReason);
    }

    // [US-2] Restating the same clinic is a no-op so the middleware and a handler can both assert it.
    [Fact]
    public void UseClinic_Twice_With_The_Same_Clinic_Is_Idempotent()
    {
        var scope = Scope();
        scope.UseClinic(ClinicA);
        scope.UseClinic(ClinicA);

        Assert.Equal(ClinicA, scope.ClinicId);
    }

    [Fact]
    public void UseSystemWide_Twice_Is_Idempotent() // [US-2]
    {
        var scope = Scope();
        scope.UseSystemWide("first");
        scope.UseSystemWide("second");

        Assert.Equal(TenantScopeKind.SystemWide, scope.Kind);
        Assert.Equal("first", scope.SystemWideReason);
    }

    [Fact]
    public void Switching_To_Another_Clinic_Throws() // [US-2]
    {
        var scope = Scope();
        scope.UseClinic(ClinicA);

        Assert.Throws<InvalidOperationException>(() => scope.UseClinic(ClinicB));
        Assert.Equal(ClinicA, scope.ClinicId);
    }

    // [US-2] The load-bearing refusal: widening is how a one-clinic path quietly turns cross-clinic.
    [Fact]
    public void Widening_A_Clinic_Scope_To_SystemWide_Throws()
    {
        var scope = Scope();
        scope.UseClinic(ClinicA);

        Assert.Throws<InvalidOperationException>(() => scope.UseSystemWide("a job, apparently"));
        Assert.Equal(TenantScopeKind.Clinic, scope.Kind);
    }

    [Fact]
    public void Narrowing_A_SystemWide_Scope_To_One_Clinic_Throws() // [US-2]
    {
        var scope = Scope();
        scope.UseSystemWide("an iterating job");

        Assert.Throws<InvalidOperationException>(() => scope.UseClinic(ClinicA));
        Assert.Equal(TenantScopeKind.SystemWide, scope.Kind);
    }

    // [US-2] Guid.Empty is the value the filter compares against when nothing was set, so accepting it here
    // would make "scoped to the empty clinic" and "unscoped" produce the same SQL.
    [Fact]
    public void UseClinic_Refuses_An_Empty_Clinic_Id()
    {
        var scope = Scope();

        Assert.Throws<ArgumentException>(() => scope.UseClinic(Guid.Empty));
        Assert.Equal(TenantScopeKind.Unset, scope.Kind);
    }

    [Fact]
    public void UseSystemWide_Refuses_A_Blank_Reason() // [US-2]
    {
        var scope = Scope();

        Assert.Throws<ArgumentException>(() => scope.UseSystemWide("  "));
        Assert.Equal(TenantScopeKind.Unset, scope.Kind);
    }
}
