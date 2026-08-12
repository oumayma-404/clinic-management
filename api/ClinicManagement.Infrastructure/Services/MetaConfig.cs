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

    /// <summary>
    /// The token Meta echoes back once, in the webhook's own subscription handshake
    /// (<c>GET ?hub.verify_token=…</c>). A secret of our choosing, and <b>separate</b> from
    /// <see cref="AppSecret"/>: this one is typed into Meta's dashboard by an operator, while the app secret signs
    /// every payload and must never be pasted anywhere.
    ///
    /// <para>⚠️ Unset means the handshake <b>always refuses</b> — never « accept anything ». The one request this
    /// gates is the one that decides whether Meta will deliver to us at all, so an absent value must fail the
    /// subscription loudly rather than register an endpoint anybody can subscribe.</para>
    /// </summary>
    public static string? WebhookVerifyToken(IConfiguration configuration) =>
        configuration["Meta:WebhookVerifyToken"];

    public static string GraphApiVersion(IConfiguration configuration) =>
        string.IsNullOrWhiteSpace(configuration["Meta:GraphApiVersion"])
            ? DefaultGraphApiVersion
            : configuration["Meta:GraphApiVersion"]!;

    /// <summary>The Graph API base URL, e.g. <c>https://graph.facebook.com/v21.0</c>.</summary>
    public static string GraphBaseUrl(IConfiguration configuration) =>
        $"https://graph.facebook.com/{GraphApiVersion(configuration)}";
}
