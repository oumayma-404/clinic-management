using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>Mark a planned act as carried out, optionally linking the dental record that recorded it.</summary>
public class MarkTreatmentPlanItemDoneCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid PlanId { get; set; }
    public Guid ItemId { get; set; }
    public DateTime? DoneOn { get; set; }
    public Guid? LinkedDentalRecordId { get; set; }

    /// <inheritdoc cref="CancelTreatmentPlanCommand.Version"/>
    /// <remarks>Marking the last act done auto-completes the devis, so this write can close a treatment
    /// somebody else is editing.</remarks>
    public uint Version { get; set; }
}

public class MarkTreatmentPlanItemDoneCommandHandler : IRequestHandler<MarkTreatmentPlanItemDoneCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IToothStateRepository _toothStateRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<MarkTreatmentPlanItemDoneCommandHandler> _logger;

    public MarkTreatmentPlanItemDoneCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IDentalRecordRepository dentalRecordRepository,
        IToothStateRepository toothStateRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<MarkTreatmentPlanItemDoneCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _dentalRecordRepository = dentalRecordRepository;
        _toothStateRepository = toothStateRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(MarkTreatmentPlanItemDoneCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var plan = await _planRepository.GetByIdAsync(request.PlanId, cancellationToken);
            if (plan == null || plan.ClinicId != clinicResult.Value)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            // Auto-close once every act is done is enforced inside MarkItemDone, so this path and the
            // record-driven one (DentalRecordLinker) behave identically.
            plan.MarkItemDone(request.ItemId, request.DoneOn ?? DateTime.UtcNow, request.LinkedDentalRecordId);

            /*
             * Marking an act realise from the workspace COMPLETES it, so the end state `ToothChartingRules`
             * was withholding becomes chartable - and nothing wrote it. The devis said « Realise » and the
             * tooth stayed blank, with no error on either side. See `ToothChartingSync`.
             *
             * Read AFTER the mark (the fiche just linked is one of them), staged on the same transaction.
             */
            var chartedItem = plan.Items.FirstOrDefault(i => i.Id == request.ItemId);
            if (chartedItem != null)
            {
                await ToothChartingSync.ApplyAsync(
                    chartedItem,
                    ToothChartingSync.EvidencingRecordIds(chartedItem),
                    clinicResult.Value,
                    _dentalRecordRepository,
                    _toothStateRepository,
                    cancellationToken);
            }

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);
            return Result<TreatmentPlanDto>.Success(plan.ToDto(patient?.GetFullName()));
        }
        catch (InvalidOperationException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error marking item done for plan {PlanId}", request.PlanId);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la mise à jour de l'acte.");
        }
    }
}
