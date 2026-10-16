namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Which OS push service a registered device is reachable through.
///
/// <para>Deliberately the <b>transport</b>, not the operating system: it is what picks the sender, and every
/// capability, credential and refusal in the push subsystem is per-platform for the same reason — a deployment
/// with a Firebase project and no Apple key can push to half its devices, and that half-configured install is
/// the likely one rather than the exotic one (spec AC-52).</para>
/// </summary>
public enum DevicePlatform
{
    /// <summary>Firebase Cloud Messaging.</summary>
    Android = 1,

    /// <summary>Apple Push Notification service.</summary>
    Ios = 2,

    /// <summary>
    /// Web Push (RFC 8030/8291) — a browser, or an iPhone home-screen app, through its vendor's push service
    /// (staff-chat Part 8). The token is the subscription's endpoint URL; its two keys live on the registration.
    /// </summary>
    WebPush = 3
}
