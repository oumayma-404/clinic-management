using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Persistence;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// `verify-schema`'s « admins without a factor holding a live session » is drift only where an administrator must hold
/// a factor. On the demo every visitor is the dentist-admin with no factor, so counting them there failed every
/// nightly rebuild that started with somebody inside (demo-cabinet story 6) and kept the deploy check red.
/// </summary>
public class AdminFactorCheckScopeTests
{
    [Theory]
    [InlineData(DeploymentKind.HostedMultiTenant, true)]
    [InlineData(DeploymentKind.ClinicRelay, true)]
    [InlineData(DeploymentKind.SelfHostedLan, false)]
    [InlineData(DeploymentKind.PublicDemo, false)]
    public void The_Check_Runs_Only_Where_An_Admin_Must_Hold_A_Factor(DeploymentKind kind, bool checks)
    {
        Assert.Equal(checks, SchemaVerificationReader.ChecksAdminSecondFactor(DeploymentProfile.For(kind)));
    }

    [Fact] // a profile the report cannot read keeps the strict reading, never the lenient one
    public void An_Unreadable_Profile_Still_Checks()
    {
        Assert.True(SchemaVerificationReader.ChecksAdminSecondFactor(null));
    }

    [Fact] // a new kind must decide; the theory above has a row for every member
    public void Every_Deployment_Kind_Is_Decided_Above()
    {
        var decided = typeof(AdminFactorCheckScopeTests).GetMethod(nameof(The_Check_Runs_Only_Where_An_Admin_Must_Hold_A_Factor))!
            .GetCustomAttributes(typeof(InlineDataAttribute), false)
            .Cast<InlineDataAttribute>()
            .Select(a => (DeploymentKind)a.GetData(null!).First()[0])
            .ToHashSet();

        Assert.Equal(Enum.GetValues<DeploymentKind>().ToHashSet(), decided);
    }
}
