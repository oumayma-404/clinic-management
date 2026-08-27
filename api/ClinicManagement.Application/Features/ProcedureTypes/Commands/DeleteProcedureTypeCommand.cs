using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.ProcedureTypes.Commands;

/// <summary>
/// Deletes an act — or ARCHIVES it when a future appointment still refers to it.
///
/// <para>⚠️ The <c>bool</c> is the outcome: <b>true = archived, false = deleted permanently.</b> It used to be
/// <c>true</c> either way, so nothing downstream could tell the two apart and the screen showed no feedback at
/// all — the row simply vanished in both cases, which is what made the dialog's wrong promise dangerous rather
/// than merely sloppy.</para>
/// </summary>
public class DeleteProcedureTypeCommand : IRequest<Result<bool>>
{
    public Guid Id { get; set; }
}

public class DeleteProcedureTypeCommandHandler : IRequestHandler<DeleteProcedureTypeCommand, Result<bool>>
{
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteProcedureTypeCommandHandler> _logger;

    public DeleteProcedureTypeCommandHandler(
        IProcedureTypeRepository procedureTypeRepository,
        IAppointmentRepository appointmentRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeleteProcedureTypeCommandHandler> logger)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _appointmentRepository = appointmentRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(DeleteProcedureTypeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var procedureType = await _procedureTypeRepository.GetByIdAsync(request.Id, cancellationToken);
            if (procedureType == null)
            {
                return Result<bool>.Failure("Type de procédure introuvable.");
            }

            // Explicit tenant check (defense-in-depth alongside the global query filter): a procedure
            // type from another clinic reads as "not found".
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<bool>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }
            if (procedureType.ClinicId != clinicResult.Value)
            {
                return Result<bool>.Failure("Type de procédure introuvable.");
            }

            // Check if used by future appointments
            var allAppointments = await _appointmentRepository.GetAllAsync(cancellationToken);
            if (procedureType.IsUsedByFutureAppointments(allAppointments))
            {
                // Soft delete instead
                _logger.LogInformation("Procedure type {ProcedureTypeId} is used by future appointments. Performing soft delete.", request.Id);
                procedureType.Deactivate();
                await _procedureTypeRepository.UpdateAsync(procedureType, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                // ⚠️ `true` = ARCHIVED. The two outcomes used to be indistinguishable to the caller — both
                // `Success(true)` — so the screen could not say which had happened and a permanent delete looked
                // exactly like a deactivation. See the controller for the shape it becomes on the wire.
                return Result<bool>.Success(true);
            }

            // Hard delete if not used
            await _procedureTypeRepository.DeleteAsync(request.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted procedure type {ProcedureTypeId}", request.Id);
            // `false` = permanently deleted.
            return Result<bool>.Success(false);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error deleting procedure type {ProcedureTypeId}", request.Id);
            return Result<bool>.Failure($"Error deleting procedure type: {ex.Message}");
        }
    }
}

