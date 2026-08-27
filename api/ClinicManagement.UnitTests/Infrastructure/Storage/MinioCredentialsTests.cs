using ClinicManagement.Infrastructure.Storage;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Storage;

/// <summary>
/// Whether MinIO counts as configured (security-hardening US-10, audit § 2 finding 11).
///
/// The finding: the committed <c>appsettings.json</c> shipped <c>minioadmin</c>/<c>minioadmin</c> and the DI
/// check treated merely <b>non-empty</b> as configured, so a Cloud deployment that forgot its env vars came up
/// silently authenticating with the published defaults instead of failing loud like every other scrubbed
/// secret. Setting the env var *to* the default was equally invisible.
/// </summary>
public class MinioCredentialsTests
{
    [Theory]
    [InlineData("minioadmin")]  // the published default — decorative, not a credential
    [InlineData("MinioAdmin")]  // case must not be an escape hatch
    [InlineData("  minioadmin  ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_default_or_blank_credential_is_not_usable(string? value)
    {
        Assert.False(MinioCredentials.IsUsable(value));
    }

    [Theory]
    [InlineData("a-real-access-key")]
    [InlineData("minioadmin2")]        // merely containing the default is fine
    [InlineData("admin")]
    public void A_real_credential_is_usable(string value)
    {
        Assert.True(MinioCredentials.IsUsable(value));
    }

    [Fact]
    public void All_three_values_must_be_present_and_real()
    {
        Assert.True(MinioCredentials.IsConfigured("localhost:9000", "real-key", "real-secret"));
    }

    [Theory]
    [InlineData(null, "real-key", "real-secret")]
    [InlineData("", "real-key", "real-secret")]
    [InlineData("localhost:9000", "minioadmin", "real-secret")]
    [InlineData("localhost:9000", "real-key", "minioadmin")]
    [InlineData("localhost:9000", "minioadmin", "minioadmin")] // what the repo shipped
    [InlineData("localhost:9000", "", "")]
    public void Anything_missing_or_default_is_not_configured(string? endpoint, string? accessKey, string? secretKey)
    {
        Assert.False(MinioCredentials.IsConfigured(endpoint, accessKey, secretKey));
    }

    // ---- The Development carve-out (AC-10.5) ----

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    public void Development_tolerates_an_unconfigured_minio(string environmentName)
    {
        // Required, not a convenience: appsettings.json is Cloud mode, docker-compose runs MinIO as
        // minioadmin, and Development.json has no override — so failing unconditionally would break
        // `dotnet run` on a fresh clone for every developer.
        Assert.True(MinioCredentials.TolerateUnconfigured(environmentName));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    [InlineData(null)]  // fails CLOSED — matches the console verbs' `?? "Production"` convention
    public void Every_other_environment_refuses_to_start(string? environmentName)
    {
        Assert.False(MinioCredentials.TolerateUnconfigured(environmentName));
    }

    [Fact]
    public void The_message_distinguishes_default_credentials_from_missing_ones()
    {
        // An operator who set the env vars to the defaults needs to hear something different from one who
        // never set them — and needs to be told to rotate.
        var usingDefaults = MinioCredentials.NotConfiguredMessage("minioadmin", "minioadmin");
        var missing = MinioCredentials.NotConfiguredMessage("", "");

        Assert.Contains("default", usingDefaults, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rotate", usingDefaults, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rotate", missing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MinIO:AccessKey", missing, StringComparison.Ordinal);
    }
}
