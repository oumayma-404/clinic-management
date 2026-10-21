using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.DoctorLeaves.Commands;

/// <summary>Removes a congé. Nothing else changes: the visits it covered were never touched.</summary>
public class DeleteDoctorLeaveCommand : IRequest<Result>
{
    public Guid DoctorId { get; set; }
    public Guid LeaveId { get; set; }
}

public class DeleteDoctorLeaveCommandHandler : IRequestHandler<DeleteDoctorLeaveCommand, Result>
{
    private readonly IDoctorLeaveRepository _leaves;
    private readonly IDoctorRepository _doctors;
    private readonly IUserRepository _users;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteDoctorLeaveCommandHandler(
        IDoctorLeaveRepository leaves,
        IDoctorRepository doctors,
        IUserRepository users,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork)
    {
        _leaves = leaves;
        _doctors = doctors;
        _users = users;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteDoctorLeaveCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var access = await DoctorLeaveRules.ResolveEditableAsync(
                _clinicContext, _users, _doctors, request.DoctorId, cancellationToken);
            if (access.IsFailure)
                return access;

            var found = await DoctorLeaveRules.ResolveLeaveAsync(_leaves, access.Value!, request.LeaveId, cancellationToken);
            if (found.IsFailure)
                return found;

            _leaves.Remove(found.Value!);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result.Failure("Erreur lors de la suppression du congé.", ex);
        }
    }
}
