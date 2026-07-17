using ClinicManagement.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Static config accessors for the TTN « El Fatoora » integration (mirrors <c>ConnectivityConfig</c> /
/// <c>RemindersConfig</c>). Non-secret settings come from the <c>Ttn:*</c> config section; the signing
/// certificate + its password and the TTN API credentials come from the per-install <c>.local/</c> store
/// or environment variables — never from committed config.
/// </summary>
public static class TtnConfig
{
    private const string DefaultCertFileName = "teif-signing.pfx";
    private const string DefaultCertPasswordFileName = "teif-signing-password";

    // --- Signing certificate (FR-2) -------------------------------------------------------------------
    /// <summary>Absolute path to the qualified-certificate PFX. Defaults to <c>.local/teif-signing.pfx</c>.</summary>
    public static string CertificatePath(IConfiguration configuration)
    {
        var configured = configuration["Ttn:CertPath"];
        return string.IsNullOrWhiteSpace(configured)
            ? LocalInstallPaths.LocalFile(DefaultCertFileName)
            : LocalInstallPaths.Resolve(configured);
    }

    /// <summary>
    /// PFX password, resolved in order: <c>TTN_CERT_PASSWORD</c> env var, <c>Ttn:CertPassword</c> config,
    /// then the <c>.local/teif-signing-password</c> file. Null when none is set.
    /// </summary>
    public static string? CertificatePassword(IConfiguration configuration)
    {
        var fromEnv = System.Environment.GetEnvironmentVariable("TTN_CERT_PASSWORD");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var fromConfig = configuration["Ttn:CertPassword"];
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig;
        }

        var passwordFile = LocalInstallPaths.LocalFile(DefaultCertPasswordFileName);
        return File.Exists(passwordFile) ? File.ReadAllText(passwordFile).Trim() : null;
    }

    // --- TTN API (FR-3, HttpTtnClient) ----------------------------------------------------------------
    /// <summary>Base URL for the given environment ("Production" → <c>Ttn:Production:BaseUrl</c>, else sandbox).</summary>
    public static string? BaseUrl(IConfiguration configuration, string environment) =>
        IsProduction(environment) ? configuration["Ttn:Production:BaseUrl"] : configuration["Ttn:Sandbox:BaseUrl"];

    public static string? TokenUrl(IConfiguration configuration, string environment) =>
        IsProduction(environment) ? configuration["Ttn:Production:TokenUrl"] : configuration["Ttn:Sandbox:TokenUrl"];

    /// <summary>OAuth2 client id / username (non-secret identifier) from config.</summary>
    public static string? Username(IConfiguration configuration) => configuration["Ttn:Username"];

    /// <summary>OAuth2 secret — <c>TTN_API_SECRET</c> env var first, then <c>Ttn:ApiSecret</c> config.</summary>
    public static string? ApiSecret(IConfiguration configuration)
    {
        var fromEnv = System.Environment.GetEnvironmentVariable("TTN_API_SECRET");
        return string.IsNullOrWhiteSpace(fromEnv) ? configuration["Ttn:ApiSecret"] : fromEnv;
    }

    // --- Outbox retry (FR-4) --------------------------------------------------------------------------
    /// <summary>Max dispatch attempts before an invoice crosses to <c>Failed</c> (default 5).</summary>
    public static int MaxAttempts(IConfiguration configuration) =>
        configuration.GetValue<int?>("Ttn:MaxAttempts") is { } v && v > 0 ? v : 5;

    /// <summary>Base backoff seconds; the next attempt is scheduled at <c>base * attemptCount</c> (default 60s).</summary>
    public static int BackoffBaseSeconds(IConfiguration configuration) =>
        configuration.GetValue<int?>("Ttn:BackoffBaseSeconds") is { } v && v > 0 ? v : 60;

    /// <summary>How many due invoices the outbox job dispatches per tick (default 20).</summary>
    public static int DispatchBatchSize(IConfiguration configuration) =>
        configuration.GetValue<int?>("Ttn:DispatchBatchSize") is { } v && v > 0 ? v : 20;

    private static bool IsProduction(string environment) =>
        string.Equals(environment?.Trim(), Clinic.TtnEnvironmentProduction, StringComparison.OrdinalIgnoreCase);
}
