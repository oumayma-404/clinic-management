using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Relay;

/// <summary>Resolves the calling PC de secours from its token, refuses a retired one, and scopes the request to its clinic.</summary>
public static class RelayPrincipal
{
    public static Guid? RelayIdOf(string? subject) =>
        subject is not null
        && subject.StartsWith(ClinicRelay.SubjectPrefix, StringComparison.Ordinal)
        && Guid.TryParse(subject.AsSpan(ClinicRelay.SubjectPrefix.Length), out var id)
            ? id
            : null;

    public static async Task<Result<ClinicRelay>> ResolveAsync(
        IClinicContext context, IClinicRelayRepository relays, ITenantScope tenantScope, CancellationToken cancellationToken)
    {
        var relayId = RelayIdOf(context.GetUserId());
        var relay = relayId is null ? null : await relays.GetByIdAcrossClinicsAsync(relayId.Value, cancellationToken);
        if (relay is null)
        {
            return Result<ClinicRelay>.Failure(RelayRefusals.UnknownRelay, RelayRefusals.UnknownRelayCode);
        }

        if (relay.Status == ClinicRelayStatus.Retired)
        {
            return Result<ClinicRelay>.Failure(RelayRefusals.Retired, RelayRefusals.RetiredCode);
        }

        tenantScope.UseClinic(relay.ClinicId);
        return Result<ClinicRelay>.Success(relay);
    }
}

/// <summary>What build this server is, so a PC de secours and its cloud run the same one (D10, D10b).</summary>
public interface IRelayBuildInfo
{
    string Current { get; }
}
