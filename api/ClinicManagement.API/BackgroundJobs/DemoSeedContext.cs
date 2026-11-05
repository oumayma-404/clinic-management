using System.Security.Claims;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Demo;
using MediatR;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// One fresh scope per command, signed in as the acting member: the journal author is fixed per scope (first read
/// wins), so a scope shared by two members would credit one with the other's work.
/// </summary>
public sealed class DemoSeedContext : IDemoSeedContext
{
    private readonly IServiceScopeFactory _scopes;

    public DemoSeedContext(IServiceScopeFactory scopes, Guid clinicId)
    {
        _scopes = scopes;
        ClinicId = clinicId;
    }

    public Guid ClinicId { get; }

    public async Task<TResponse> SendAsAsync<TResponse>(
        DemoMember member, IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { RequestServices = services, User = Principal(member, ClinicId) };
        try
        {
            services.GetRequiredService<ITenantScope>().UseClinic(ClinicId);
            return await services.GetRequiredService<ISender>().Send(request, cancellationToken);
        }
        finally
        {
            accessor.HttpContext = previous;
        }
    }

    // The claim types a self-issued token carries (LocalAuthService.GenerateToken), so ClinicContext reads them alike.
    private static ClaimsPrincipal Principal(DemoMember member, Guid clinicId) =>
        new(new ClaimsIdentity(
            new[]
            {
                new Claim("sub", member.UserId),
                new Claim(ClaimTypes.NameIdentifier, member.UserId),
                new Claim("clinic_id", clinicId.ToString()),
                new Claim("role", member.Role),
                new Claim("email", member.Email),
            },
            authenticationType: "DemoSeed"));
}
