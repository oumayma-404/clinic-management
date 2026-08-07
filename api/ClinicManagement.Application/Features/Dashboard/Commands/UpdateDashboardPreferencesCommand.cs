using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Dashboard.Commands;

/// <summary>
/// Replaces the signed-in user's hidden-block set.
///
/// <para>
/// <b>Replace, not patch.</b> The caller is a settings panel that always knows the full intended state, and a
/// merge could not express "show this one again" — the only way back would be a second, delete-shaped endpoint.
/// </para>
/// <para>
/// This command lives under <c>Features.Dashboard.Commands</c>, which would normally make
/// <c>RealtimeBroadcastBehavior</c> emit a <c>dashboard</c> realtime key derived from the namespace. It does not,
/// because <c>Dashboard</c> is in <c>RealtimeResourceResolver.ExcludedAreas</c> — and that is deliberate on two
/// counts. A broadcast goes to the whole <c>clinic-{id}</c> group, so one user hiding a card would tell every
/// other user's browser to refetch; and this is per-user UI state, not clinic data any list view mirrors, which
/// is the exact rationale the exclusion list already carries for Auth / AI / Backup / Connectivity.
/// </para>
/// </summary>
public class UpdateDashboardPreferencesCommand : IRequest<Result<DashboardPreferencesDto>>
{
    /// <summary>
    /// The blocks to hide. <c>null</c> and an empty list both mean "hide nothing" — a client resetting to defaults
    /// may legitimately send either, and treating one of them as "no change" would make the reset button silently
    /// do nothing.
    /// </summary>
    public IReadOnlyList<string>? HiddenKpis { get; set; }
}

public class UpdateDashboardPreferencesCommandHandler
    : IRequestHandler<UpdateDashboardPreferencesCommand, Result<DashboardPreferencesDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserDashboardPreferenceRepository _preferences;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateDashboardPreferencesCommandHandler> _logger;

    public UpdateDashboardPreferencesCommandHandler(
        IClinicContext clinicContext,
        IUserDashboardPreferenceRepository preferences,
        IUnitOfWork unitOfWork,
        ILogger<UpdateDashboardPreferencesCommandHandler> logger)
    {
        _clinicContext = clinicContext;
        _preferences = preferences;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<DashboardPreferencesDto>> Handle(
        UpdateDashboardPreferencesCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Result<DashboardPreferencesDto>.Failure("Utilisateur non authentifié.");
            }

            var submitted = request.HiddenKpis ?? Array.Empty<string>();

            // An unknown key is REFUSED, not silently dropped. Dropping it would let a frontend that renamed a KPI
            // appear to save successfully while hiding nothing — the user toggles a card off, the panel reports
            // success, and the card is still there on the next load with no explanation anywhere.
            var unknown = submitted
                .Where(k => DashboardKpiKeys.Normalize(k) is null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (unknown.Length > 0)
            {
                return Result<DashboardPreferencesDto>.Failure(
                    $"Élément(s) inconnu(s) du tableau de bord : {string.Join(", ", unknown)}.");
            }

            var normalized = submitted
                .Select(k => DashboardKpiKeys.Normalize(k)!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            // Refusing to hide *everything* is not paternalism — a dashboard with no blocks is a blank page whose
            // only affordance is the customiser that emptied it, and the user has no way to tell it apart from a
            // failed load. Leaving at least one block means the page always explains itself.
            if (normalized.Length >= DashboardKpiKeys.All.Count)
            {
                return Result<DashboardPreferencesDto>.Failure(
                    "Au moins un élément doit rester affiché sur le tableau de bord.");
            }

            var preference = await _preferences.GetByUserIdAsync(userId, cancellationToken);
            if (preference is null)
            {
                // Created on first save, not on first read — see GetDashboardPreferencesQuery.
                preference = new UserDashboardPreference(userId);
                preference.SetHiddenKpis(normalized);
                await _preferences.AddAsync(preference, cancellationToken);
            }
            else
            {
                preference.SetHiddenKpis(normalized);
                await _preferences.UpdateAsync(preference, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Echo the persisted state rather than the request: the entity canonicalises (sorts, de-dupes, caps),
            // so what came back from it is what a later GET will return. Echoing the request instead would let the
            // client hold a shape the server does not actually have.
            return Result<DashboardPreferencesDto>.Success(
                new DashboardPreferencesDto(preference.HiddenKpis(), DashboardKpiKeys.All));
        }
        catch (Exception ex) when (ex is not Common.Exceptions.ConflictException)
        {
            _logger.LogError(ex, "Failed to save dashboard preferences");
            return Result<DashboardPreferencesDto>.Failure("Impossible d'enregistrer les préférences du tableau de bord.");
        }
    }
}
