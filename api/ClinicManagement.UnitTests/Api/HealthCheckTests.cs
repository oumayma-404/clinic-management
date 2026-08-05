using ClinicManagement.API.Startup;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The two <c>/health</c> checks (multi-tenant-cloud US-6).
///
/// <para><b>The grading is the substance, not the plumbing.</b> The framework answers 200 for Healthy <i>and</i>
/// Degraded and 503 for Unhealthy, so whether a check returns Degraded or Unhealthy decides whether an
/// orchestrator pulls the instance out of rotation. Storage being down must not: a clinic can still take
/// appointments, record fiches and collect money, and restarting the API does not bring MinIO back — grading it
/// Unhealthy would turn a partial outage into a total one. The database being down must: with no database the
/// process can serve nothing.</para>
///
/// <para>The healthy database path is not asserted here — nothing in this project touches a database. What is
/// asserted is that an unreachable one produces a <i>report</i> rather than an unhandled exception, which is the
/// failure mode that would make the endpoint useless exactly when it is needed.</para>
/// </summary>
public class HealthCheckTests
{
    private static readonly HealthCheckContext Context = new();

    /// <summary>A connection that refuses immediately — port 1 is never listening.</summary>
    private static ApplicationDbContext UnreachableDatabase() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1")
            .Options, null);

    private static IServiceProvider ProviderFor(IFileStorage storage)
    {
        var services = new ServiceCollection();
        services.AddSingleton(storage);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task An_unreachable_database_is_unhealthy_rather_than_an_unhandled_exception()
    {
        await using var context = UnreachableDatabase();
        var check = new DatabaseHealthCheck(context, NullLogger<DatabaseHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(Context);

        // Unhealthy → 503, which is what stops an orchestrator sending traffic to an instance that cannot serve.
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public async Task Reachable_storage_is_healthy()
    {
        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.ProbeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var check = new FileStorageHealthCheck(
            ProviderFor(storage.Object), NullLogger<FileStorageHealthCheck>.Instance);

        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(Context)).Status);
    }

    [Fact]
    public async Task Unreachable_storage_is_degraded_and_NOT_unhealthy()
    {
        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.ProbeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bucket introuvable"));

        var check = new FileStorageHealthCheck(
            ProviderFor(storage.Object), NullLogger<FileStorageHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(Context);

        // Degraded still answers 200. Deliberate: see the class summary.
        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task Storage_that_cannot_even_be_RESOLVED_is_degraded_rather_than_a_500()
    {
        // Where MinIO is unconfigured, AddInfrastructure deliberately registers an IFileStorage factory that
        // THROWS. A constructor-injected storage would therefore throw while the framework was building the
        // check — a 500 from /health instead of a report naming which half is down.
        var services = new ServiceCollection();
        services.AddScoped<IFileStorage>(_ => throw new InvalidOperationException("MinIO is not configured."));

        var check = new FileStorageHealthCheck(
            services.BuildServiceProvider(), NullLogger<FileStorageHealthCheck>.Instance);

        Assert.Equal(HealthStatus.Degraded, (await check.CheckHealthAsync(Context)).Status);
    }

    [Fact]
    public void The_route_sits_outside_api()
    {
        // Load-bearing where the front door is self-hosted: a /api/health would be measured by the client-version
        // floor and the API rate limiter, and would collide with the controller routes.
        Assert.Equal("/health", HealthChecks.Path);
        Assert.False(HealthChecks.Path.StartsWith("/api", StringComparison.OrdinalIgnoreCase));
    }
}
