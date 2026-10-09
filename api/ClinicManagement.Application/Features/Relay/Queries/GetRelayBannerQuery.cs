using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using MediatR;

namespace ClinicManagement.Application.Features.Relay.Queries;

/// <summary>
/// The strip on every screen while a cut is under way (<c>clinic-pc-copy</c> AC-3.4, AC-4.1, AC-5.2, EC-20, D20): one
/// short title the phone shows alone, and a detail. <c>Kind</c> is what the browser branches on (its icon and tone),
/// never the sentence; <c>Warning</c> is false for the « on its way back » kinds, which ask nothing of anyone.
/// </summary>
public sealed record RelayBannerDto(string Kind, string Title, string Detail, bool Warning);

/// <summary>Null when nothing is under way — the browser then shows no strip.</summary>
public sealed record GetRelayBannerQuery : IRequest<Result<RelayBannerDto?>>;

public sealed class GetRelayBannerQueryHandler : IRequestHandler<GetRelayBannerQuery, Result<RelayBannerDto?>>
{
    private readonly ICurrentClinicResolver _clinic;
    private readonly IClinicRelayRepository _relays;
    private readonly IRelayLocalStatus _local;

    public GetRelayBannerQueryHandler(ICurrentClinicResolver clinic, IClinicRelayRepository relays, IRelayLocalStatus local)
    {
        _clinic = clinic;
        _relays = relays;
        _local = local;
    }

    public async Task<Result<RelayBannerDto?>> Handle(GetRelayBannerQuery request, CancellationToken cancellationToken)
    {
        // On the PC de secours: what it knows of itself, from its lease (no database read).
        if (_local.IsHandingBack || _local.IsHolding)
        {
            return Result<RelayBannerDto?>.Success(RelayBanners.OnThePc(_local.IsHandingBack, _local.CutCause));
        }

        var clinicId = await _clinic.GetClinicIdAsync();
        if (clinicId.IsFailure)
        {
            return Result<RelayBannerDto?>.FailureFrom(clinicId);
        }

        var relay = await _relays.GetCurrentForClinicAsync(clinicId.Value, cancellationToken);
        return Result<RelayBannerDto?>.Success(RelayBanners.OnTheCloud(relay, DateTime.UtcNow));
    }
}

/// <summary>The banner's words, in one place — the 423 sentences say the same thing to a person who pressed a button.</summary>
public static class RelayBanners
{
    public const string PcHolding = "pc-holding";
    public const string PcReturning = "pc-returning";
    public const string CloudOnRelay = "cloud-on-relay";
    public const string CloudReturning = "cloud-returning";
    public const string CloudSilent = "cloud-silent";
    public const string CloudRestoring = "cloud-restoring";

    /// <summary>AC-3.4 / EC-20: the PC says which — the cabinet's internet, or the cloud — and that nothing is lost.</summary>
    public static RelayBannerDto OnThePc(bool handingBack, string? cutCause) =>
        handingBack
            ? new(PcReturning, "Retour au cloud en cours", "un enregistrement refusé maintenant passera dans quelques secondes.", false)
            : cutCause == "cloud"
                ? new(PcHolding, "Le cloud est injoignable",
                    "le cabinet travaille sur le PC de secours. Tout partira dans le cloud à son retour.", true)
                : new(PcHolding, "Internet coupé",
                    "le cabinet travaille sur le PC de secours. Tout partira dans le cloud au retour d'internet.", true);

    /// <summary>AC-4.1 and the cloud's other fenced states; null when the cloud records this cabinet's work.</summary>
    public static RelayBannerDto? OnTheCloud(ClinicRelay? relay, DateTime nowUtc)
    {
        if (relay is null || !ClinicWriteLease.IsCloudFenced(relay, nowUtc))
        {
            return null;
        }

        if (relay.IsReturning)
        {
            return new(CloudReturning, "Retour au cloud en cours", "un enregistrement refusé maintenant passera dans quelques secondes.", false);
        }

        if (relay.IsRecoveringGap(nowUtc))
        {
            return new(CloudRestoring, "Le cloud récupère les données du PC de secours",
                "après une restauration : réessayez dans quelques instants.", false);
        }

        if (relay.PcHoldingSinceUtc is { } since)
        {
            var at = RelayLabels.Moment(since, nowUtc);
            return new(CloudOnRelay, $"Le cabinet travaille sur le PC de secours depuis {at}",
                $"ici, lecture seule. Données arrêtées à {at}.", true);
        }

        var silentSince = ClinicWriteLease.LockedSinceUtc(relay, nowUtc) ?? nowUtc;
        return new(CloudSilent, $"Le PC de secours ne répond plus depuis {RelayLabels.Moment(silentSince, nowUtc)}",
            "ici, lecture seule. Un administrateur peut « Reprendre la main » dans Paramètres → PC de secours.", true);
    }
}
