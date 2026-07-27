namespace ClinicManagement.Infrastructure.Storage;

/// <summary>
/// Decides whether MinIO is genuinely configured (security-hardening US-10, audit § 2 finding 11).
///
/// <para>The committed <c>appsettings.json</c> shipped <c>minioadmin</c>/<c>minioadmin</c>, and the DI check
/// treated merely <b>non-empty</b> as configured — so a Cloud deployment that forgot its env vars came up
/// silently authenticating with the published default credentials instead of failing loud like every other
/// scrubbed secret. Setting the env var *to* the default was equally invisible.</para>
///
/// <para>So "configured" now means present <b>and not a known default</b>. A credential that is only
/// decorative is treated as absent.</para>
/// </summary>
public static class MinioCredentials
{
    /// <summary>The published MinIO default, which must never count as a real credential.</summary>
    public const string KnownDefault = "minioadmin";

    /// <summary>The ASP.NET environment name under which default credentials are tolerated.</summary>
    public const string DevelopmentEnvironment = "Development";

    /// <summary>
    /// True when a single credential value is usable: non-blank and not the published default.
    /// </summary>
    public static bool IsUsable(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value.Trim(), KnownDefault, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the endpoint and both credentials are usable.</summary>
    public static bool IsConfigured(string? endpoint, string? accessKey, string? secretKey) =>
        !string.IsNullOrWhiteSpace(endpoint) && IsUsable(accessKey) && IsUsable(secretKey);

    /// <summary>
    /// Whether an unconfigured (or default-credentialled) MinIO should be <b>tolerated</b> rather than fail
    /// startup.
    ///
    /// <para>Only in Development, and this carve-out is required rather than a convenience: the tracked
    /// <c>appsettings.json</c> sets <c>Auth:Mode=Cloud</c>, <c>docker-compose.yml</c> runs MinIO as
    /// <c>minioadmin</c>/<c>minioadmin</c>, and <c>appsettings.Development.json</c> carries no MinIO override —
    /// so failing loud unconditionally would break <c>dotnet run</c> on a fresh clone for every developer
    /// (spec AC-10.5).</para>
    ///
    /// <para>A missing environment name reads as <b>not</b> Development, matching the convention the console
    /// verbs already use (<c>?? "Production"</c>) and failing closed rather than open.</para>
    /// </summary>
    public static bool TolerateUnconfigured(string? environmentName) =>
        string.Equals(environmentName?.Trim(), DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase);

    /// <summary>The operator-facing startup failure. Names what to set, not just what is wrong.</summary>
    public static string NotConfiguredMessage(string? accessKey, string? secretKey)
    {
        var usingDefaults = string.Equals(accessKey?.Trim(), KnownDefault, StringComparison.OrdinalIgnoreCase)
            || string.Equals(secretKey?.Trim(), KnownDefault, StringComparison.OrdinalIgnoreCase);

        return usingDefaults
            ? "MinIO is configured with the published default credentials ('minioadmin'), which is equivalent "
              + "to having no credentials at all. Set MinIO:Endpoint, MinIO:AccessKey and MinIO:SecretKey to "
              + "real values (environment variables or user-secrets), and rotate any deployment that has been "
              + "running on the defaults."
            : "MinIO is not configured. Set MinIO:Endpoint, MinIO:AccessKey and MinIO:SecretKey (environment "
              + "variables or user-secrets). Cloud file storage cannot start without them.";
    }
}
