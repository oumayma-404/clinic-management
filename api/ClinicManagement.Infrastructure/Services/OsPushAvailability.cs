using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// <see cref="IOsPushAvailability"/> over the resolved deployment profile and the <c>Push</c> configuration —
/// the one place the topology half and the credentials half are combined.
/// </summary>
public sealed class OsPushAvailability : IOsPushAvailability
{
    private readonly DeploymentProfile _profile;
    private readonly IConfiguration _configuration;

    public OsPushAvailability(DeploymentProfile profile, IConfiguration configuration)
    {
        _profile = profile;
        _configuration = configuration;
    }

    public bool SupportsPush(DevicePlatform platform) =>
        _profile.PermitsOsPush(platform) && PushConfig.Resolve(_configuration, platform).IsConfigured;

    public bool IsAvailableAtAll =>
        Enum.GetValues<DevicePlatform>().Any(SupportsPush);

    public string? UnavailableReason(DevicePlatform platform)
    {
        if (!_profile.PermitsOsPush(platform))
        {
            // Names the installation rather than the platform: nothing an operator configures changes this
            // answer, so « ajoutez vos identifiants » would send them looking for a setting that cannot help.
            return "Les notifications système ne sont pas disponibles sur cette installation.";
        }

        if (!PushConfig.Resolve(_configuration, platform).IsConfigured)
        {
            return $"Les notifications {Label(platform)} ne sont pas configurées sur ce serveur.";
        }

        return null;
    }

    /// <summary>What a French sentence calls the platform — « Android »/« iOS », not the enum member.</summary>
    internal static string Label(DevicePlatform platform) => platform switch
    {
        DevicePlatform.Android => "Android",
        DevicePlatform.Ios => "iOS",
        _ => platform.ToString()
    };
}
