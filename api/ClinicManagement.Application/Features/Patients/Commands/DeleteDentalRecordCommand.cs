using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class DeleteDentalRecordCommand : IRequest<Result<bool>>
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
}

public class DeleteDentalRecordCommandHandler : IRequestHandler<DeleteDentalRecordCommand, Result<bool>>
{
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteDentalRecordCommandHandler(
        IDentalRecordRepository dentalRecordRepository,
        IUnitOfWork unitOfWork)
    {
        _dentalRecordRepository = dentalRecordRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(DeleteDentalRecordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var dentalRecord = await _dentalRecordRepository.GetByIdAsync(request.Id, cancellationToken);
            if (dentalRecord == null)
            {
                return Result<bool>.Failure("Dental record not found");
            }

            if (dentalRecord.PatientId != request.PatientId)
            {
                return Result<bool>.Failure("Dental record does not belong to the specified patient");
            }

            await _dentalRecordRepository.DeleteAsync(request.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Error deleting dental record: {ex.Message}");
        }
    }
}









