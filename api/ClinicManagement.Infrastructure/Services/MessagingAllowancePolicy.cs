using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// <see cref="IMessagingAllowancePolicy"/> over the <c>Messaging</c> configuration section.
///
/// <para>⚠️ <b>Parsed by hand rather than through <c>GetValue&lt;int&gt;</c>, which <i>throws</i> on a value it
/// cannot convert.</b> <see cref="DefaultMessagesPerMonth"/> is read while a cabinet is being <b>provisioned</b>, so
/// a typo in the operator's config would abort clinic creation with a binder exception instead of falling back —
/// <c>SubscriptionPolicy.TrialDays</c>' reasoning, and the « a mistyped setting must refuse nothing, never
/// everything » rule <c>ClientVersionMiddleware</c> already follows.</para>
/// </summary>
public sealed class MessagingAllowancePolicy : IMessagingAllowancePolicy
{
    /// <summary>
    /// R-12's provisional figure. Public so the rollout migration's backfill and this fallback cannot disagree —
    /// the scaffolded-default trap `backup-schedule-backfill` exists to catch.
    /// </summary>
    public const int DefaultMonthlyAllowance = 200;

    /// <summary>A guard on the operator's value, not a policy: a million messages is a typo, and 0 is a real choice.</summary>
    private const int MaxMessagesPerMonth = 1_000_000;

    private readonly IConfiguration _configuration;

    public MessagingAllowancePolicy(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public int DefaultMessagesPerMonth =>
        int.TryParse(_configuration["Messaging:DefaultMessagesPerMonth"], out var configured)
        && configured is >= 0 and <= MaxMessagesPerMonth
            ? configured
            : DefaultMonthlyAllowance;

    public string? ContactEmail => Published("Messaging:ContactEmail");

    public string? ContactPhone => Published("Messaging:ContactPhone");

    /// <summary>Whitespace is not a contact route: an unset key and a blank one mean the same thing here.</summary>
    private string? Published(string key)
    {
        var value = _configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
