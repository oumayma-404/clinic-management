using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// <see cref="IVendorMessagingAvailability"/> over the resolved deployment profile and the <c>Meta</c> section —
/// the one place the topology half and the credentials half are combined.
/// </summary>
public sealed class VendorMessagingAvailability : IVendorMessagingAvailability
{
    private readonly DeploymentProfile _profile;
    private readonly IConfiguration _configuration;

    public VendorMessagingAvailability(DeploymentProfile profile, IConfiguration configuration)
    {
        _profile = profile;
        _configuration = configuration;
    }

    public bool SellsVendorMessaging => _profile.SellsVendorMessaging;

    /// <summary>
    /// Both halves. The credentials are the app id and secret the Embedded-Signup code exchange needs — without
    /// them the browser's connect button no-ops and the server could not complete the exchange anyway.
    /// </summary>
    public bool CanOnboardCabinets =>
        SellsVendorMessaging
        && !string.IsNullOrWhiteSpace(MetaConfig.AppId(_configuration))
        && !string.IsNullOrWhiteSpace(MetaConfig.AppSecret(_configuration));
}
