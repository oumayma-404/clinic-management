using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.ProcedureTypes.Commands;

public class DeleteProcedureTypeCommand : IRequest<Result<bool>>
{
    public Guid Id { get; set; }
}

public class DeleteProcedureTypeCommandHandler : IRequestHandler<DeleteProcedureTypeCommand, Result<bool>>
{
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteProcedureTypeCommandHandler> _logger;

    public DeleteProcedureTypeCommandHandler(
        IProcedureTypeRepository procedureTypeRepository,
        IAppointmentRepository appointmentRepository,
        IUnitOfWork unitOfWork,
        ILogger<DeleteProcedureTypeCommandHandler> logger)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _appointmentRepository = appointmentRepository;
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
                return Result<bool>.Failure("Procedure type not found");
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
                return Result<bool>.Success(true);
            }

            // Hard delete if not used
            await _procedureTypeRepository.DeleteAsync(request.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted procedure type {ProcedureTypeId}", request.Id);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting procedure type {ProcedureTypeId}", request.Id);
            return Result<bool>.Failure($"Error deleting procedure type: {ex.Message}");
        }
    }
}

