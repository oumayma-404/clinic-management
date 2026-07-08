using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure;

/// <summary>
/// Verifies the <c>IFileStorage</c> backend is chosen by <c>Auth:Mode</c>:
/// Local (offline) → local disk; Cloud → MinIO (FR-C1/C2). Resolves the seam from a real
/// <c>AddInfrastructure</c> registration without touching any external service.
/// </summary>
public class InfrastructureFileStorageWiringTests
{
    private const string DummyConnection = "Host=localhost;Database=clinic;Username=u;Password=p";

    private static IFileStorage ResolveFileStorage(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        using var scope = services.BuildServiceProvider().CreateScope();
        return scope.ServiceProvider.GetRequiredService<IFileStorage>();
    }

    // [AC-1] Local mode resolves the local-disk backend (no MinIO configured).
    [Fact]
    public void LocalMode_Resolves_LocalDiskFileStorage()
    {
        var fileStorage = ResolveFileStorage(new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "Local",
            ["ConnectionStrings:DefaultConnection"] = DummyConnection,
            ["FileStorage:BasePath"] = Path.Combine(Path.GetTempPath(), "clinic-wiring-tests"),
        });

        Assert.IsType<LocalDiskFileStorage>(fileStorage);
    }

    // [AC-2] Cloud mode with MinIO configured resolves the MinIO backend (unchanged behavior).
    [Fact]
    public void CloudMode_With_Minio_Resolves_MinioFileStorage()
    {
        var fileStorage = ResolveFileStorage(new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "Cloud",
            ["ConnectionStrings:DefaultConnection"] = DummyConnection,
            ["MinIO:Endpoint"] = "localhost:9000",
            ["MinIO:AccessKey"] = "access",
            ["MinIO:SecretKey"] = "secret",
        });

        Assert.IsType<MinioFileStorage>(fileStorage);
    }
}
