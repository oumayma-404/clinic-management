using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Dashboard.Commands;

/// <summary>
/// The signed-in user ticks — or unticks — a « Premiers pas » step BY HAND (<c>guided-first-days</c>, owner's call
/// 2026-11-04): the step's circle, « Terminé » on the phone window, the end of the fiche tour. « Fait, ou au moins
/// compris »: some steps cannot be done on a cabinet's first days.
///
/// <para>⚠️ Every step but the security code (<see cref="GettingStartedRules.TickableSteps"/>). Per-user UI state on the
/// <see cref="UserDashboardPreference"/> row, so — like <see cref="UpdateDashboardPreferencesCommand"/> — no realtime
/// broadcast (<c>Dashboard</c> is an excluded area). Unticking a step its own data says is done changes nothing on the
/// card, which is why the card offers untick only for a step done by hand.</para>
/// </summary>
public class SetGettingStartedStepTickCommand : IRequest<Result>
{
    /// <summary>The « Premiers pas » step key (« dentalRecord », « phoneApp », …).</summary>
    public string? Step { get; set; }

    public bool Ticked { get; set; } = true;
}

public static class GettingStartedTickRefusals
{
    public const string NotTickableCode = "getting_started_step_not_tickable";
    public const string NotTickable = "Cette étape ne se coche pas à la main.";
}

public class SetGettingStartedStepTickCommandHandler : IRequestHandler<SetGettingStartedStepTickCommand, Result>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserDashboardPreferenceRepository _preferences;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetGettingStartedStepTickCommandHandler> _logger;

    public SetGettingStartedStepTickCommandHandler(
        IClinicContext clinicContext,
        IUserDashboardPreferenceRepository preferences,
        IUnitOfWork unitOfWork,
        ILogger<SetGettingStartedStepTickCommandHandler> logger)
    {
        _clinicContext = clinicContext;
        _preferences = preferences;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(SetGettingStartedStepTickCommand request, CancellationToken cancellationToken)
    {
        var step = request.Step?.Trim();
        if (step is null || !GettingStartedRules.TickableSteps.Contains(step))
        {
            return Result.Failure(GettingStartedTickRefusals.NotTickable, GettingStartedTickRefusals.NotTickableCode);
        }

        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Result.Failure(ErrorMessages.Generic);
            }

            var preference = await _preferences.GetByUserIdAsync(userId, cancellationToken);
            if (preference is null)
            {
                // Nothing to untick on a row that does not exist yet.
                if (!request.Ticked) return Result.Success();
                preference = new UserDashboardPreference(userId);
                preference.TickStep(step);
                await _preferences.AddAsync(preference, cancellationToken);
            }
            else
            {
                if (request.Ticked) preference.TickStep(step);
                else preference.UntickStep(step);
                await _preferences.UpdateAsync(preference, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Ticking a getting-started step failed.");
            return Result.Failure(ErrorMessages.Generic);
        }
    }
}
