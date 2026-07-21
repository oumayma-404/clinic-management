namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Drives the Meta Embedded-Signup provisioning steps against the WhatsApp Business (Graph) API using the
/// platform app credentials (Cloud onboarding only). Each step throws <see cref="WhatsAppOnboardingException"/>
/// (carrying a <see cref="WhatsAppOnboardingError"/> category) on failure so the caller can keep the connect
/// flow atomic — nothing is persisted until every step succeeds. Implemented in Infrastructure.
/// </summary>
public interface IWhatsAppOnboardingService
{
    /// <summary>Exchanges the one-time Embedded-Signup <paramref name="code"/> for a business access token.</summary>
    Task<string> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Subscribes the platform app to the clinic's WABA so it can send on that account.</summary>
    Task SubscribeAppAsync(string wabaId, string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Registers the phone number on Cloud API (Meta requires a registration PIN).</summary>
    Task RegisterPhoneAsync(string phoneNumberId, string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Best-effort unsubscribe on disconnect. Implementations should not throw on failure.</summary>
    Task UnsubscribeAppAsync(string wabaId, string accessToken, CancellationToken cancellationToken = default);
}

/// <summary>Classifies an onboarding failure so the caller can surface a specific, safe message.</summary>
public enum WhatsAppOnboardingError
{
    /// <summary>The code → token exchange failed (bad/expired code, app-credential problem).</summary>
    CodeExchangeFailed = 0,

    /// <summary>The phone number is already registered elsewhere or needs migration.</summary>
    NumberAlreadyRegistered = 1,

    /// <summary>The WABA is not verified / not eligible to send.</summary>
    WabaNotEligible = 2,

    /// <summary>Any other provisioning failure (subscribe/register/network).</summary>
    Unknown = 3
}

/// <summary>Thrown by <see cref="IWhatsAppOnboardingService"/> when a provisioning step fails.</summary>
public sealed class WhatsAppOnboardingException : Exception
{
    public WhatsAppOnboardingException(WhatsAppOnboardingError error, string message)
        : base(message)
    {
        Error = error;
    }

    public WhatsAppOnboardingError Error { get; }
}
