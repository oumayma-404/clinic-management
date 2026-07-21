using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Config accessors for the Meta WhatsApp Embedded-Signup onboarding (the <c>Meta</c> section). Mirrors the
/// <see cref="RemindersConfig"/> idiom: static accessors over <see cref="IConfiguration"/> with baked-in
/// defaults. <c>Meta:AppSecret</c> is a secret and is expected to come from the environment
/// (<c>Meta__AppSecret</c>) / user-secrets — never committed appsettings. Absent config is not a startup
/// failure: onboarding simply cannot run until the values are present.
/// </summary>
public static class MetaConfig
{
    private const string DefaultGraphApiVersion = "v21.0";

    public static string? AppId(IConfiguration configuration) => configuration["Meta:AppId"];
    public static string? AppSecret(IConfiguration configuration) => configuration["Meta:AppSecret"];

    public static string GraphApiVersion(IConfiguration configuration) =>
        string.IsNullOrWhiteSpace(configuration["Meta:GraphApiVersion"])
            ? DefaultGraphApiVersion
            : configuration["Meta:GraphApiVersion"]!;

    /// <summary>The Graph API base URL, e.g. <c>https://graph.facebook.com/v21.0</c>.</summary>
    public static string GraphBaseUrl(IConfiguration configuration) =>
        $"https://graph.facebook.com/{GraphApiVersion(configuration)}";
}
