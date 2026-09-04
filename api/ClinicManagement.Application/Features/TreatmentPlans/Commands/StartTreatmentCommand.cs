using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// « Suivre ce traitement » — start following a multi-séance act, in one press, from wherever the dentist is.
///
/// <para>
/// It creates the treatment as a <b>Draft</b>: no number, no échéancier, no créance. That is the whole point.
/// <c>CreateTreatmentPlanCommand</c> numbers and accepts in the same save (« il est validé et numéroté dès sa
/// création »), and <c>Accept</c> then raises a lump-sum échéance for the entire total — so booking an implant
/// through it produced a numbered, accepted document claiming 800,000 DT from a dialog whose subject was a
/// visit, with a number that can only ever be cancelled with a motif. Measured, on a real booking: 2026-0023.
/// </para>
///
/// <para>
/// A Draft is safe to be this: <c>Number</c> is nullable with its unique index filtered to non-null (so many
/// coexist), and <c>PlanBillingRules.CarriesDebt</c> excludes Draft from every money read — the treatment
/// carries no claim <b>by construction</b>. The number is taken later, and only by
/// <see cref="IssueDevisCommand"/>, when the patient actually asks for the paper.
/// </para>
///
/// <para>
/// ⚠️ <b>One act, deliberately.</b> This is the cheap door, not a devis editor: the dentist is looking at a
/// séance, not drawing up a plan. Several acts, teeth, an échéancier and a title are what
/// <c>CreateTreatmentPlanCommand</c> is for, and both produce the same aggregate — so a treatment started here
/// is edited there with nothing to migrate.
/// </para>
/// </summary>
public class StartTreatmentCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid PatientId { get; set; }

    /// <summary>The catalogue act being followed. Its protocol becomes the séances; its tarif the total.</summary>
    public Guid ProcedureTypeId { get; set; }

    /// <summary>
    /// The total agreed with the patient for the whole act. Null takes the catalogue tarif.
    /// <para>
    /// ⚠️ This is the treatment's <b>only</b> price. A séance of it has none — what a séance carries is an
    /// <i>encaissement</i>, which draws this figure down and never adds to it. Typing 600 on the first séance
    /// of a 2 000 DT implant means « 600 collected, 1 400 left », not « 2 600 ».
    /// </para>
    /// </summary>
    public decimal? AgreedTotal { get; set; }

    /// <summary>FDI teeth, when the act is tooth-specific. Empty is legitimate (a whole-mouth act).</summary>
    public List<int> ToothNumbers { get; set; } = new();
}

public class StartTreatmentCommandHandler : IRequestHandler<StartTreatmentCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<StartTreatmentCommandHandler> _logger;

    public StartTreatmentCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<StartTreatmentCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        StartTreatmentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Patient introuvable.");
            }

            var procedure = await _procedureTypeRepository.GetByIdAsync(request.ProcedureTypeId, cancellationToken);
            if (procedure == null || procedure.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Acte introuvable.");
            }

            var total = request.AgreedTotal ?? procedure.DefaultCost ?? 0m;
            if (total < 0m)
            {
                return Result<TreatmentPlanDto>.Failure("Le total convenu ne peut pas être négatif.");
            }

            // The act's own name is the treatment's title — the dentist named it by picking it, and asking for
            // a title as well would be a second field for one fact.
            var plan = new TreatmentPlan(Guid.NewGuid(), clinicId, request.PatientId, procedure.Name);
            plan.SetItems(new[]
            {
                (procedure.Name, total, (IReadOnlyList<int>)request.ToothNumbers.Distinct().ToList()),
            });

            // Relink the line to its catalogue act, so the protocol below and every later re-price can find it.
            var item = plan.Items.Single();
            plan.UpdateItems(new[]
            {
                new TreatmentPlanItemInput(
                    item.Id, procedure.Name, total, procedure.Id, request.ToothNumbers.Distinct().ToList()),
            });

            /*
             * The séances, from the catalogue protocol — this is the « automate whenever we could » half. An act
             * with no protocol gets no steps and behaves exactly as it always did, which is most acts.
             *
             * ⚠️ Applied HERE rather than by `TreatmentPlanStepProtocol` on acceptance, because this treatment
             * is never accepted: it stays a Draft until somebody asks for the devis.
             */
            var protocol = procedure.DefaultSteps;
            if (protocol.Count > 0)
            {
                plan.SetItemSteps(item.Id, protocol.Select(s =>
                    new TreatmentPlanItemStepInput(null, s.Label, s.DurationMinutes, s.MinDaysAfterPrevious)));
            }

            await _planRepository.AddAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Started draft treatment {PlanId} for patient {PatientId} on {Act} — {Steps} séances, {Total} DT, no number",
                plan.Id, plan.PatientId, procedure.Name, protocol.Count, total);

            return Result<TreatmentPlanDto>.Success(plan.ToDto(patient.GetFullName()));
        }
        catch (InvalidOperationException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error starting treatment for patient {PatientId}", request.PatientId);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la création du traitement.");
        }
    }
}
