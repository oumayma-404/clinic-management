using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Relay.Commands;

/// <summary>
/// D16: the PC de secours's long poll — it says which numbers it has just kept, and waits (up to
/// <see cref="Wait"/>) for the next ones a save on the cloud is about to make final. The relay area broadcasts nothing,
/// which matters here: the poll is re-opened every few seconds.
/// </summary>
public sealed record ExchangeRelayPromisesCommand(IReadOnlyList<Guid>? Acks)
    : IRequest<Result<IReadOnlyList<RelayNumberPromiseDto>>>
{
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);
}

public sealed class ExchangeRelayPromisesCommandHandler
    : IRequestHandler<ExchangeRelayPromisesCommand, Result<IReadOnlyList<RelayNumberPromiseDto>>>
{
    private const int MaxAcks = 200;

    private readonly IClinicContext _clinicContext;
    private readonly IClinicRelayRepository _relays;
    private readonly ITenantScope _tenantScope;
    private readonly IRelayPromiseBroker _broker;

    public ExchangeRelayPromisesCommandHandler(
        IClinicContext clinicContext, IClinicRelayRepository relays, ITenantScope tenantScope, IRelayPromiseBroker broker)
    {
        _clinicContext = clinicContext;
        _relays = relays;
        _tenantScope = tenantScope;
        _broker = broker;
    }

    public async Task<Result<IReadOnlyList<RelayNumberPromiseDto>>> Handle(
        ExchangeRelayPromisesCommand request, CancellationToken cancellationToken)
    {
        var resolved = await RelayPrincipal.ResolveAsync(_clinicContext, _relays, _tenantScope, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyList<RelayNumberPromiseDto>>.FailureFrom(resolved);
        }

        var acks = (request.Acks ?? Array.Empty<Guid>()).Take(MaxAcks).ToList();
        var promises = await _broker.ExchangeAsync(resolved.Value!.Id, acks, ExchangeRelayPromisesCommand.Wait, cancellationToken);
        return Result<IReadOnlyList<RelayNumberPromiseDto>>.Success(promises);
    }
}
