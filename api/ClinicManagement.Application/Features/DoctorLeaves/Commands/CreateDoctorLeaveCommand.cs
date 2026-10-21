using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.DoctorLeaves.Commands;

/// <summary>Records a congé for one practitioner. Visits already booked inside it are counted, never moved.</summary>
public class CreateDoctorLeaveCommand : IRequest<Result<DoctorLeaveSaveResultDto>>
{
    public Guid DoctorId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Note { get; set; }
}

public class CreateDoctorLeaveCommandHandler : IRequestHandler<CreateDoctorLeaveCommand, Result<DoctorLeaveSaveResultDto>>
{
    private readonly IDoctorLeaveRepository _leaves;
    private readonly IDoctorRepository _doctors;
    private readonly IAppointmentRepository _appointments;
    private readonly IUserRepository _users;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public CreateDoctorLeaveCommandHandler(
        IDoctorLeaveRepository leaves,
        IDoctorRepository doctors,
        IAppointmentRepository appointments,
        IUserRepository users,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork)
    {
        _leaves = leaves;
        _doctors = doctors;
        _appointments = appointments;
        _users = users;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<DoctorLeaveSaveResultDto>> Handle(CreateDoctorLeaveCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (DoctorLeave.RefusalFor(request.StartDate, request.EndDate, request.Note) is { } refusal)
                return Result<DoctorLeaveSaveResultDto>.Failure(refusal);

            var access = await DoctorLeaveRules.ResolveEditableAsync(
                _clinicContext, _users, _doctors, request.DoctorId, cancellationToken);
            if (access.IsFailure)
                return Result<DoctorLeaveSaveResultDto>.FailureFrom(access);
            var doctor = access.Value!;

            if (!doctor.IsActive)
                return Result<DoctorLeaveSaveResultDto>.Failure(DoctorLeaveRules.RetiredPractitioner);

            var overlap = await _leaves.FindOverlappingAsync(
                doctor.ClinicId, doctor.Id, request.StartDate, request.EndDate, cancellationToken: cancellationToken);
            if (overlap != null)
                return Result<DoctorLeaveSaveResultDto>.Failure(DoctorLeaveRules.OverlapSentence(doctor.FullName, overlap));

            // Counted before the save, so a failed count can never report an error over a congé already stored.
            var during = await DoctorLeaveRules.AppointmentsDuringAsync(
                _appointments, doctor.ClinicId, doctor.Id, request.StartDate, request.EndDate, cancellationToken);
            var leave = new DoctorLeave(
                Guid.NewGuid(), doctor.ClinicId, doctor.Id, request.StartDate, request.EndDate, request.Note);
            await _leaves.AddAsync(leave, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<DoctorLeaveSaveResultDto>.Success(DoctorLeaveRules.SaveResult(leave, doctor.FullName, during));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<DoctorLeaveSaveResultDto>.Failure("Erreur lors de l'enregistrement du congé.", ex);
        }
    }
}
