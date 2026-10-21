using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.DoctorLeaves.Commands;

/// <summary>Changes a congé's dates or motif. A congé already under way, or past, can still be corrected.</summary>
public class UpdateDoctorLeaveCommand : IRequest<Result<DoctorLeaveSaveResultDto>>
{
    public Guid DoctorId { get; set; }
    public Guid LeaveId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Note { get; set; }

    /// <summary>The version the client read; <c>0</c> means « not supplied » (see <c>IUnitOfWork.SetExpectedVersion</c>).</summary>
    public uint Version { get; set; }
}

public class UpdateDoctorLeaveCommandHandler : IRequestHandler<UpdateDoctorLeaveCommand, Result<DoctorLeaveSaveResultDto>>
{
    private readonly IDoctorLeaveRepository _leaves;
    private readonly IDoctorRepository _doctors;
    private readonly IAppointmentRepository _appointments;
    private readonly IUserRepository _users;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateDoctorLeaveCommandHandler(
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

    public async Task<Result<DoctorLeaveSaveResultDto>> Handle(UpdateDoctorLeaveCommand request, CancellationToken cancellationToken)
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

            var found = await DoctorLeaveRules.ResolveLeaveAsync(_leaves, doctor, request.LeaveId, cancellationToken);
            if (found.IsFailure)
                return Result<DoctorLeaveSaveResultDto>.FailureFrom(found);
            var leave = found.Value!;

            var overlap = await _leaves.FindOverlappingAsync(
                doctor.ClinicId, doctor.Id, request.StartDate, request.EndDate, leave.Id, cancellationToken);
            if (overlap != null)
                return Result<DoctorLeaveSaveResultDto>.Failure(DoctorLeaveRules.OverlapSentence(doctor.FullName, overlap));

            // Counted before the save, so a failed count can never report an error over a change already stored.
            var during = await DoctorLeaveRules.AppointmentsDuringAsync(
                _appointments, doctor.ClinicId, doctor.Id, request.StartDate, request.EndDate, cancellationToken);
            leave.Change(request.StartDate, request.EndDate, request.Note);
            _unitOfWork.SetExpectedVersion(leave, request.Version);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<DoctorLeaveSaveResultDto>.Success(DoctorLeaveRules.SaveResult(leave, doctor.FullName, during));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<DoctorLeaveSaveResultDto>.Failure("Erreur lors de la modification du congé.", ex);
        }
    }
}
