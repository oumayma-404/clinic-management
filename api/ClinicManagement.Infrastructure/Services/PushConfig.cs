using ClinicManagement.Domain.Enums;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// The per-install OS-push credentials, resolved for one platform. Passed to a sender so it never reads
/// <c>IConfiguration</c> itself — the same rule the reminder senders follow, which is what keeps a channel's
/// « why will this not send? » answerable from one predicate instead of from inside an HTTP call.
/// </summary>
public sealed record ResolvedPushCredentials(
    DevicePlatform Platform,
    string? Endpoint,
    string? Identity,
    string? Secret)
{
    /// <summary>All three present ⇒ this platform can be sent to. The single sendability predicate.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(Identity)
        && !string.IsNullOrWhiteSpace(Secret);
}

/// <summary>
/// Static accessors over the <c>Push</c> configuration section, mirroring <c>RemindersConfig</c> and
/// <c>TtnConfig</c>.
///
/// <para><b>Per install, not per clinic</b>: there is one mobile app, so one Firebase project and one Apple team
/// for the whole deployment. Secrets are expected from the environment
/// (<c>Push__Fcm__ServiceAccountKey</c>, <c>Push__Apns__PrivateKey</c>) — committed config carries empty strings,
/// like every other channel's credentials.</para>
/// </summary>
public static class PushConfig
{
    private const string DefaultFcmEndpoint = "https://fcm.googleapis.com/v1/projects/{projectId}/messages:send";
    private const string DefaultApnsEndpoint = "https://api.push.apple.com/3/device/{token}";

    public static ResolvedPushCredentials Resolve(IConfiguration configuration, DevicePlatform platform) =>
        platform switch
        {
            DevicePlatform.Android => new ResolvedPushCredentials(
                platform,
                Value(configuration, "Push:Fcm:ApiUrl") ?? DefaultFcmEndpoint,
                Value(configuration, "Push:Fcm:ProjectId"),
                Value(configuration, "Push:Fcm:ServiceAccountKey")),

            DevicePlatform.Ios => new ResolvedPushCredentials(
                platform,
                Value(configuration, "Push:Apns:ApiUrl") ?? DefaultApnsEndpoint,
                // The topic APNs rejects a mismatch on — the shell's bundle id, which Part 8 settles.
                Value(configuration, "Push:Apns:BundleId"),
                Value(configuration, "Push:Apns:PrivateKey")),

            _ => new ResolvedPushCredentials(platform, null, null, null)
        };

    /// <summary>How many queued sends one tick may attempt overall.</summary>
    public static int DispatchBatchSize(IConfiguration configuration) =>
        Positive(configuration["Push:DispatchBatchSize"], 100);

    /// <summary>
    /// How many of one clinic's sends a single tick may take. The reminder queue's fairness bound, here from the
    /// start rather than after a shared install starved.
    /// </summary>
    public static int PerClinicDispatchBound(IConfiguration configuration) =>
        Positive(configuration["Push:PerClinicDispatchBound"], 25);

    public static int MaxAttempts(IConfiguration configuration) =>
        Positive(configuration["Push:MaxAttempts"], 3);

    /// <summary>
    /// How long a terminal row is kept. Shorter than the reminder outbox's 90 days: a push carries no message
    /// worth reading back, so its only value after the fact is diagnosing a delivery complaint.
    /// </summary>
    public static int RetentionDays(IConfiguration configuration) =>
        Positive(configuration["Push:RetentionDays"], 30);

    private static string? Value(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int Positive(string? raw, int fallback) =>
        int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : fallback;
}
