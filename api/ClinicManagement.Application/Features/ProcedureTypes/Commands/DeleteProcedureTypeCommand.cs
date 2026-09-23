using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.ProcedureTypes.Commands;

/// <summary>
/// Deletes an act — or ARCHIVES it when a future appointment or a devis line still refers to it.
///
/// <para>⚠️ <see cref="ProcedureTypeDeletion.Archived"/> is the outcome: <b>true = archived, false = deleted
/// permanently.</b> It used to be <c>true</c> either way, so nothing downstream could tell the two apart and the
/// screen showed no feedback at all — the row simply vanished in both cases.</para>
/// <para>⚠️ A devis line archives it too (I4): the line keeps the act's id, and deleting the act took its colour,
/// duration, protocol and prefill with it — the one consumer the dialog never mentioned.</para>
/// </summary>
public class DeleteProcedureTypeCommand : IRequest<Result<ProcedureTypeDeletion>>
{
    public Guid Id { get; set; }
}

/// <summary>What a delete did, and what kept the act alive when it was archived instead.</summary>
public sealed record ProcedureTypeDeletion(bool Archived, int FutureAppointments, int PlanLines);

public class DeleteProcedureTypeCommandHandler : IRequestHandler<DeleteProcedureTypeCommand, Result<ProcedureTypeDeletion>>
{
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteProcedureTypeCommandHandler> _logger;
    private readonly ITreatmentPlanRepository? _planRepository;

    public DeleteProcedureTypeCommandHandler(
        IProcedureTypeRepository procedureTypeRepository,
        IAppointmentRepository appointmentRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeleteProcedureTypeCommandHandler> logger,
        ITreatmentPlanRepository? planRepository = null)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _appointmentRepository = appointmentRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _planRepository = planRepository;
    }

    public async Task<Result<ProcedureTypeDeletion>> Handle(DeleteProcedureTypeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var procedureType = await _procedureTypeRepository.GetByIdAsync(request.Id, cancellationToken);
            if (procedureType == null)
            {
                return Result<ProcedureTypeDeletion>.Failure("Type de procédure introuvable.");
            }

            // Explicit tenant check (defense-in-depth alongside the global query filter): a procedure
            // type from another clinic reads as "not found".
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<ProcedureTypeDeletion>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }
            if (procedureType.ClinicId != clinicResult.Value)
            {
                return Result<ProcedureTypeDeletion>.Failure("Type de procédure introuvable.");
            }

            // Only this act's visits, filtered in SQL — it used to load every appointment of the clinic (I4).
            var futureAppointments = procedureType.CountFutureAppointments(
                await _appointmentRepository.GetByProcedureTypeIdAsync(procedureType.Id, cancellationToken));
            var planLines = _planRepository is null
                ? 0
                : await _planRepository.CountItemsUsingProcedureTypeAsync(
                    clinicResult.Value, procedureType.Id, cancellationToken);
            if (futureAppointments > 0 || planLines > 0)
            {
                // Soft delete instead
                _logger.LogInformation(
                    "Procedure type {ProcedureTypeId} is used by {Appointments} future appointments and {PlanLines} devis lines. Archiving.",
                    request.Id, futureAppointments, planLines);
                procedureType.Deactivate();
                await _procedureTypeRepository.UpdateAsync(procedureType, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                // ⚠️ ARCHIVED. The two outcomes used to be indistinguishable to the caller, so a permanent delete
                // looked exactly like a deactivation. See the controller for the shape it becomes on the wire.
                return Result<ProcedureTypeDeletion>.Success(new ProcedureTypeDeletion(true, futureAppointments, planLines));
            }

            // Hard delete if not used
            await _procedureTypeRepository.DeleteAsync(request.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted procedure type {ProcedureTypeId}", request.Id);
            // Permanently deleted.
            return Result<ProcedureTypeDeletion>.Success(new ProcedureTypeDeletion(false, 0, 0));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error deleting procedure type {ProcedureTypeId}", request.Id);
            return Result<ProcedureTypeDeletion>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

