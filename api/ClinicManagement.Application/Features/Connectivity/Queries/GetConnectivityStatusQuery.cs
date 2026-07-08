using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Features.Connectivity.Queries;

public class GetConnectivityStatusQuery : IRequest<Result<ConnectivityStatusDto>>
{
}

public class GetConnectivityStatusQueryHandler : IRequestHandler<GetConnectivityStatusQuery, Result<ConnectivityStatusDto>>
{
    private readonly IInternetProbe _internetProbe;

    public GetConnectivityStatusQueryHandler(IInternetProbe internetProbe)
    {
        _internetProbe = internetProbe;
    }

    public async Task<Result<ConnectivityStatusDto>> Handle(GetConnectivityStatusQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var reachable = await _internetProbe.IsInternetReachableAsync(cancellationToken);
            return Result<ConnectivityStatusDto>.Success(new ConnectivityStatusDto { InternetReachable = reachable });
        }
        catch (Exception)
        {
            // A probe failure is itself a "no internet" signal, not a query error — never surface a 500
            // for a connectivity poll (the frontend polls this on an interval).
            return Result<ConnectivityStatusDto>.Success(new ConnectivityStatusDto { InternetReachable = false });
        }
    }
}
