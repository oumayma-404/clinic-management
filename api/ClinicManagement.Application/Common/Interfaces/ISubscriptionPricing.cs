using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// What « Abonnement » tells a cabinet it costs and how to pay (AC-2.1, AC-2.4).
///
/// <para><b>Per-deployment configuration, never compiled in</b> — AC-2.4 says so outright, and a price in a C#
/// literal is a price that needs a release to correct. An Application-side seam for the reason
/// <see cref="IPublicAppUrlProvider"/> is one: this project references no configuration package.</para>
///
/// <para>Every member is allowed to be absent. A deployment that has not filled the section in shows the cabinet
/// its state and its date and says the price is not published, which is a true statement — inventing a figure
/// would not be.</para>
/// </summary>
public interface ISubscriptionPricing
{
    /// <summary>The monthly price in dinars, or null when this deployment publishes none for that forfait.</summary>
    decimal? MonthlyPrice(SubscriptionPlan plan);

    /// <summary>The annual price in dinars, or null. Not derived from the monthly one — an annual rate is a discount.</summary>
    decimal? AnnualPrice(SubscriptionPlan plan);

    /// <summary>The French text telling the cabinet how to pay (bank details, what reference to quote).</summary>
    string? PaymentInstructions { get; }

    string? ContactEmail { get; }

    string? ContactPhone { get; }
}
