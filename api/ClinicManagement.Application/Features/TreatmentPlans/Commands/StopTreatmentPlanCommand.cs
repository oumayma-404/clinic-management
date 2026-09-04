using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// « Arrêter le traitement » — the patient is not continuing. Parks every act with no delivered work, keeps the
/// rest, re-spreads the échéancier onto the kept total and closes the devis, in <b>one</b> command.
/// <para>
/// ⚠️ <b>It replaces two sequential client calls, and each of the three defects that shape produced was real.</b>
/// The client filtered the plan's acts by their derived workflow état — which answers for an act's <i>next
/// step</i> — then called <c>amend</c> to remove them and <c>complete</c> to close the plan. So a bridge with two
/// of three séances delivered was offered for deletion under a dialog promising « ce qui a déjà été fait est
/// conservé »; the removal <b>deleted</b> the act's step rows and the links to the two fiches that evidenced
/// them; and because the clôture is a second request it threw <i>after</i> the removals had committed, leaving
/// the acts gone, the échéancier rewritten, the plan still open and « Arrêter » no longer on screen. Measured on
/// a purpose-built devis: 1 060 DT → 60 DT, three step rows → none, two orphaned <c>DentalRecords</c>, and three
/// <c>AppointmentProcedures</c> rows pointing at ids that no longer exist. Only SQL put it back.
/// </para>
/// <para>
/// The whole transition is <see cref="Domain.Entities.TreatmentPlan.StopTreatment"/>'s, so there is no window in
/// which half of it has happened, and the acts are <b>parked rather than deleted</b> — which is what makes
/// <see cref="ReopenTreatmentPlanCommand"/> possible.
/// </para>
/// </summary>
public class StopTreatmentPlanCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }

    /// <summary>
    /// The <c>Version</c> the client read, round-tripped so the stop is checked against the copy the user was
    /// shown. It matters here more than on most commands: the dialog lists the acts it is about to park, and a
    /// colleague recording a séance in between would change which of them have delivered work.
    /// </summary>
    public uint Version { get; set; }
}

public class StopTreatmentPlanCommandHandler
    : IRequestHandler<StopTreatmentPlanCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<StopTreatmentPlanCommandHandler> _logger;

    public StopTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<StopTreatmentPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        StopTreatmentPlanCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var plan = await _planRepository.GetByIdAsync(request.Id, cancellationToken);
            if (plan == null || plan.ClinicId != clinicResult.Value)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            // `ClinicClock`, never `DateTime.Today` — the re-spread échéance is a calendar day in Tunisia, and
            // the client used to build it from the browser's own clock, which dates it to yesterday for the
            // first hour of every Tunisian day and makes it « En retard » the moment it is written.
            var parked = plan.StopTreatment(ClinicClock.ClinicToday());

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Stopped treatment plan {PlanId}: {ParkedCount} act(s) withdrawn, kept total {Total}",
                plan.Id, parked.Count, plan.TotalPlanned);

            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);
            return Result<TreatmentPlanDto>.Success(plan.ToDto(patient?.GetFullName()));
        }
        catch (InvalidOperationException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error stopping treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de l'arrêt du traitement.");
        }
    }
}
