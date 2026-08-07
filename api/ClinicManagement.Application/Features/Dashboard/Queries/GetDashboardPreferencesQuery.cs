using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Dashboard.Queries;

/// <summary>
/// The signed-in user's dashboard layout choices, plus the set of blocks the dashboard can show.
/// </summary>
public class GetDashboardPreferencesQuery : IRequest<Result<DashboardPreferencesDto>>
{
}

public class GetDashboardPreferencesQueryHandler
    : IRequestHandler<GetDashboardPreferencesQuery, Result<DashboardPreferencesDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserDashboardPreferenceRepository _preferences;
    private readonly ILogger<GetDashboardPreferencesQueryHandler> _logger;

    public GetDashboardPreferencesQueryHandler(
        IClinicContext clinicContext,
        IUserDashboardPreferenceRepository preferences,
        ILogger<GetDashboardPreferencesQueryHandler> logger)
    {
        _clinicContext = clinicContext;
        _preferences = preferences;
        _logger = logger;
    }

    public async Task<Result<DashboardPreferencesDto>> Handle(
        GetDashboardPreferencesQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Result<DashboardPreferencesDto>.Failure("Utilisateur non authentifié.");
            }

            // No row is the normal state for anyone who has never customised anything, so it is NOT an error and
            // NOT created here: a GET that writes would give every user who merely loaded the dashboard a row.
            var stored = await _preferences.GetByUserIdAsync(userId, cancellationToken);

            // Stored keys are filtered against the current set on the way out. A key left behind by a KPI that has
            // since been removed would otherwise travel to the client, which would render a switch for a block that
            // no longer exists — and toggling it would be the only way to get rid of a row nobody can see.
            var hidden = (stored?.HiddenKpis() ?? Array.Empty<string>())
                .Select(DashboardKpiKeys.Normalize)
                .Where(k => k is not null)
                .Select(k => k!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return Result<DashboardPreferencesDto>.Success(
                new DashboardPreferencesDto(hidden, DashboardKpiKeys.All));
        }
        catch (Exception ex) when (ex is not Common.Exceptions.ConflictException)
        {
            _logger.LogError(ex, "Failed to read dashboard preferences");
            return Result<DashboardPreferencesDto>.Failure("Impossible de charger les préférences du tableau de bord.");
        }
    }
}
