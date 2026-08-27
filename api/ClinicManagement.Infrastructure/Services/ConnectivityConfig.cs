using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Config accessors for the internet-reachability probe (Local-mode connectivity signal).
/// Mirrors the <see cref="Auth.LocalAuthConfig"/> idiom: static accessors over
/// <see cref="IConfiguration"/> with baked-in <c>const</c> defaults, so the feature works with no
/// <c>Connectivity</c> section present in appsettings.
/// </summary>
public static class ConnectivityConfig
{
    private const string DefaultProbeUrl = "https://www.google.com/generate_204";
    private const int DefaultProbeTimeoutSeconds = 3;
    private const int DefaultProbeCacheSeconds = 5;

    /// <summary>URL the server probes to judge internet egress. A reliable HTTP 204 endpoint by default.</summary>
    public static string ProbeUrl(IConfiguration configuration) =>
        configuration["Connectivity:ProbeUrl"] ?? DefaultProbeUrl;

    /// <summary>Hard timeout for a single probe so a poll never hangs.</summary>
    public static int ProbeTimeoutSeconds(IConfiguration configuration) =>
        configuration.GetValue<int?>("Connectivity:ProbeTimeoutSeconds") ?? DefaultProbeTimeoutSeconds;

    /// <summary>How long a probe result is cached/shared so N clients don't hammer the probe URL (R-1).</summary>
    public static int ProbeCacheSeconds(IConfiguration configuration) =>
        configuration.GetValue<int?>("Connectivity:ProbeCacheSeconds") ?? DefaultProbeCacheSeconds;
}
