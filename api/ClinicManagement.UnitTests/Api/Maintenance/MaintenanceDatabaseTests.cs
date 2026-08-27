using ClinicManagement.API.Maintenance;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicManagement.UnitTests.Api.Maintenance;

/// <summary>
/// The gate the database-reading console verbs share, after amendment M3 (multi-tenant-cloud US-6).
///
/// <para><b>What changed and why it mattered.</b> <c>verify-schema</c>, <c>reconcile-money</c> and
/// <c>reset-admin-password</c> used to refuse unless <c>HasLocalDbTooling</c> — a capability about
/// <c>pg_dump</c>/<c>pg_restore</c> being on the box, which none of them runs. Their own refusal messages already
/// said « needs a direct database connection », so the gate and the message disagreed and the gate was wrong. Two
/// consequences, both bad: <c>verify-schema</c> was unreachable in <c>HostedMultiTenant</c> — and it is the
/// <b>only</b> gate a schema change has in this product, since nothing in this test project touches a database —
/// and a hosted clinic's locked-out admin had no recovery once <c>provision-clinic</c> could create one.</para>
///
/// <para>So the test that matters here is the profile-independence one: the gate must answer the same in all three
/// topologies, because the question is about configuration and not about where the app runs.</para>
/// </summary>
public class MaintenanceDatabaseTests
{
    private static IConfiguration Config(string? connectionString, string? profile = null)
    {
        var values = new List<KeyValuePair<string, string?>>
        {
            new("ConnectionStrings:DefaultConnection", connectionString)
        };

        if (profile is not null)
        {
            values.Add(new KeyValuePair<string, string?>(DeploymentProfile.ProfileKey, profile));
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void A_configured_connection_string_passes()
    {
        Assert.True(MaintenanceDatabase.HasConnectionString(
            Config("Host=db;Database=clinic;Username=u;Password=p"), "verify-schema"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_connection_string_refuses(string? connectionString)
    {
        Assert.False(MaintenanceDatabase.HasConnectionString(Config(connectionString), "verify-schema"));
    }

    [Theory]
    [InlineData(nameof(DeploymentKind.SelfHostedLan))]
    [InlineData(nameof(DeploymentKind.HostedMultiTenant))]
    public void The_answer_does_not_depend_on_the_deployment_profile(string profile)
    {
        // The point of M3. Before it, HostedMultiTenant refused — taking the product's only schema gate away from
        // the one topology where a bad migration affects every clinic at once.
        Assert.True(MaintenanceDatabase.HasConnectionString(
            Config("Host=db;Database=clinic;Username=u;Password=p", profile), "verify-schema"));
    }

    [Fact]
    public void The_refusal_names_the_key_an_operator_has_to_set()
    {
        var stderr = new StringWriter();
        var original = Console.Error;

        try
        {
            Console.SetError(stderr);
            MaintenanceDatabase.HasConnectionString(Config(null), "verify-schema");
        }
        finally
        {
            Console.SetError(original);
        }

        var message = stderr.ToString();

        // « needs a database connection » with no key name leaves the operator guessing which of four config
        // layers to edit.
        Assert.Contains("verify-schema", message, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings:DefaultConnection", message, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__DefaultConnection", message, StringComparison.Ordinal);
    }
}
