using MediatR;

namespace ClinicManagement.Application.Features.Demo;

/// <summary>A team member the demo scenario acts as: the journal names them, and their role is the one checked.</summary>
public sealed record DemoMember(string UserId, string Email, string Role);

/// <summary>
/// Sends a real command as one member of the demo cabinet (<c>demo-cabinet</c> plan D5): the identity, the clinic and
/// the journal author all agree, exactly as for a request that member made. Implemented in the API host.
/// </summary>
public interface IDemoSeedContext
{
    Guid ClinicId { get; }

    Task<TResponse> SendAsAsync<TResponse>(DemoMember member, IRequest<TResponse> request, CancellationToken cancellationToken);
}
