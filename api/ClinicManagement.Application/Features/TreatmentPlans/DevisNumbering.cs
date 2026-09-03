using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans;

/// <summary>
/// Assigns a devis its gapless <c>AAAA-NNNN</c> number and commits, retrying on a unique-index collision.
/// <para>
/// Shared because acceptance now happens in **two** places: <c>CreateTreatmentPlanCommand</c> (every new plan is
/// accepted on creation) and <c>AcceptTreatmentPlanCommand</c> (the legacy drafts that predate that). A gapless
/// per-clinic-per-year sequence with a recompute-and-retry loop is not something to hold in two copies — the day
/// they drift, one of them starts leaving holes in a numbering the clinic has to be able to defend.
/// </para>
/// <para>
/// It also applies each act's catalogue step protocol (<see cref="TreatmentPlanStepProtocol"/>), for the same
/// reason and in the same breath: acceptance is the first instant a devis act may hold steps at all, and
/// leaving the protocol to the callers is how one of them ends up numbering a devis whose « Couronne / bridge »
/// arrives with no étape on it. Numbering and protocol are one operation here — « accepter le devis » — so
/// there is no second call for a caller to forget.
/// </para>
/// </summary>
public static class DevisNumbering
{
    private const int MaxAttempts = 5;

    /// <param name="persist">
    /// Stages the plan — <c>AddAsync</c> from the create path, <c>UpdateAsync</c> from the accept path. Re-run on
    /// each attempt, which is safe either way: the aggregate is already tracked after the first call.
    /// </param>
    public static async Task<Result> AcceptAndSaveAsync(
        TreatmentPlan plan,
        Guid clinicId,
        ITreatmentPlanRepository planRepository,
        IProcedureTypeRepository procedureTypeRepository,
        IUnitOfWork unitOfWork,
        Func<CancellationToken, Task> persist,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // The clinic's fiscal year, not the UTC one (AC-P6.8): a devis accepted at 00:30 on 1 January Tunis
        // belongs to the year that just opened, not the one that just closed.
        var year = ClinicClock.ClinicYear();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var nextSequence = await planRepository.GetMaxSequenceForYearAsync(clinicId, year, cancellationToken) + 1;
            var number = $"{year}-{nextSequence:D4}";

            // Accept() is only legal from Draft, so a retry re-numbers instead of re-accepting.
            if (attempt == 1)
            {
                plan.Accept(number);

                // Immediately after Accept() and before the first persist: SetItemSteps refuses a Draft, and
                // the steps have to be on the aggregate for the retry loop to re-save them with the new number.
                await TreatmentPlanStepProtocol.ApplyAsync(
                    plan, clinicId, procedureTypeRepository, cancellationToken);
            }
            else
            {
                plan.SetAcceptedNumber(number);
            }

            await persist(cancellationToken);

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                logger.LogWarning(
                    "Devis number {Number} collided on attempt {Attempt}; recomputing", number, attempt);
            }
        }

        return Result.Failure("Impossible d'attribuer un numéro de devis unique. Veuillez réessayer.");
    }
}
