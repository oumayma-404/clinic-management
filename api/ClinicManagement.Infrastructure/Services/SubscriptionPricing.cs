using System.Globalization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// <see cref="ISubscriptionPricing"/> over the <c>Subscription</c> section: per-forfait prices under
/// <c>Subscription:Plans:&lt;Plan&gt;:{PriceMonthlyDt,PriceAnnualDt}</c>, plus the payment instructions and the
/// contact details.
///
/// <para>Its own accessor class on <c>SmtpConfig</c>'s one-section-per-class rule — a class that reads two config
/// sections is how a key ends up looked up under the wrong prefix. Nothing here is a secret, so all of it may live
/// in committed config; the real figures arrive through the operator-owned layer.</para>
///
/// <para>A non-positive or unparseable price reads as <b>absent</b> rather than as « 0,000 DT »: « le tarif n'est
/// pas publié » is a true statement about a deployment that has not filled the section in, and a zero is not.</para>
/// </summary>
public class SubscriptionPricing : ISubscriptionPricing
{
    private readonly IConfiguration _configuration;

    public SubscriptionPricing(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public decimal? MonthlyPrice(SubscriptionPlan plan) => Price(plan, "PriceMonthlyDt");

    public decimal? AnnualPrice(SubscriptionPlan plan) => Price(plan, "PriceAnnualDt");

    public string? PaymentInstructions => Trimmed(_configuration["Subscription:PaymentInstructions"]);

    public string? ContactEmail => Trimmed(_configuration["Subscription:ContactEmail"]);

    public string? ContactPhone => Trimmed(_configuration["Subscription:ContactPhone"]);

    /// <summary>
    /// ⚠️ Parsed by hand, and <b>invariant</b>. <c>GetValue&lt;decimal?&gt;</c> throws on a value it cannot convert,
    /// which would turn a typo in a price into a 500 on the one screen an expired cabinet needs — a price is
    /// information, so an unreadable one must read as « non publié », never as a failure. Invariant culture because
    /// a config file is not localised: on a fr-TN host <c>"120.5"</c> would otherwise parse as 1205.
    /// </summary>
    private decimal? Price(SubscriptionPlan plan, string key)
    {
        var raw = _configuration[$"Subscription:Plans:{plan}:{key}"];

        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var configured)
               && configured > 0
            ? configured
            : null;
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
