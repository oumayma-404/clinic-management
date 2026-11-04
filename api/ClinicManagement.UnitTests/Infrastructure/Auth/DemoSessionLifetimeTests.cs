using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Auth;

/// <summary>
/// demo-cabinet FR-3: a demo session ends at a fixed time — the credentials minted for it must not outlive that end,
/// and the lifetime itself is read from configuration without ever failing the deployment on a typo.
/// </summary>
public class DemoSessionLifetimeTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(
                settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            {
                ["Auth:Local:SigningKey"] = "demo-test-signing-key-0123456789abcdef0123456789",
                ["Auth:Mode"] = "Local",
            })
            .Build();

    private static User Dentist() =>
        User.CreateLocalUser(Guid.NewGuid(), User.RoleAdmin, "monica@demo.apexa.tn", "hash", "Monica Geller");

    [Fact] // [FR-3] a cap nearer than the ordinary lifetime wins, for both credentials
    public void Both_Credentials_Expire_No_Later_Than_The_Session_End()
    {
        var service = new LocalAuthService(Configuration());
        var end = DateTime.UtcNow.AddMinutes(5);

        var refresh = service.GenerateRefreshToken(Dentist(), Guid.NewGuid(), trusted: false, end);
        var access = service.GenerateToken(Dentist(), Guid.NewGuid(), end);

        Assert.Equal(end, refresh.ExpiresAtUtc);
        Assert.Equal(end, access.ExpiresAtUtc);
    }

    [Fact] // a cap further away than the ordinary lifetime changes nothing
    public void A_Distant_Cap_Leaves_The_Ordinary_Lifetime()
    {
        var service = new LocalAuthService(Configuration());
        var far = DateTime.UtcNow.AddDays(10);

        var access = service.GenerateToken(Dentist(), Guid.NewGuid(), far);

        Assert.True(access.ExpiresAtUtc < DateTime.UtcNow.AddHours(1));
    }

    [Theory] // the lifetime is configurable, and a typo falls back rather than stopping the demo from starting
    [InlineData(null, DemoPolicy.DefaultSessionMinutes)]
    [InlineData("45", 45)]
    [InlineData("deux heures", DemoPolicy.DefaultSessionMinutes)]
    [InlineData("0", DemoPolicy.DefaultSessionMinutes)]
    [InlineData("100000", DemoPolicy.DefaultSessionMinutes)]
    public void The_Session_Lifetime_Is_Read_Safely(string? configured, int expectedMinutes)
    {
        var settings = configured is null ? Array.Empty<(string, string)>() : new[] { (DemoPolicy.SessionMinutesKey, configured) };

        var policy = new DemoPolicy(DeploymentProfile.For(DeploymentKind.PublicDemo), Configuration(settings));

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), policy.SessionLifetime);
        Assert.True(policy.RefusesWrites);
        Assert.True(policy.IssuesDemoSessions);
    }

    [Theory] // no configuration can make another kind a demo
    [InlineData(DeploymentKind.SelfHostedLan)]
    [InlineData(DeploymentKind.HostedMultiTenant)]
    [InlineData(DeploymentKind.ClinicRelay)]
    public void Only_The_Demo_Kind_Refuses_Writes_Or_Issues_Demo_Sessions(DeploymentKind kind)
    {
        var policy = new DemoPolicy(DeploymentProfile.For(kind), Configuration((DemoPolicy.SessionMinutesKey, "30")));

        Assert.False(policy.RefusesWrites);
        Assert.False(policy.IssuesDemoSessions);
    }
}
