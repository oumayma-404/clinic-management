using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Users.Commands;

/// <summary>
/// Admin-only: deactivates or reactivates a clinic user (AC-5.3). A deactivated user can no
/// longer log in, but their historical records are retained (no data is deleted).
/// </summary>
public class SetUserActiveCommand : IRequest<Result<ClinicUserDto>>
{
    public string TargetUserId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class SetUserActiveCommandHandler : IRequestHandler<SetUserActiveCommand, Result<ClinicUserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public SetUserActiveCommandHandler(
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ClinicUserDto>> Handle(SetUserActiveCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<ClinicUserDto>.Failure("User ID not found in token");
            }

            var admin = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin == null)
            {
                return Result<ClinicUserDto>.Failure("User not found");
            }

            // AC-5.4: only an admin can (de)activate users.
            if (!admin.IsAdmin())
            {
                return Result<ClinicUserDto>.Failure("Only admins can change a user's status");
            }

            if (string.IsNullOrWhiteSpace(request.TargetUserId))
            {
                return Result<ClinicUserDto>.Failure("Target user is required.");
            }

            // An admin deactivating themselves would be an unrecoverable lockout in Phase 1
            // (the recovery utility resets a password, not the active flag), so block it.
            if (!request.IsActive && string.Equals(request.TargetUserId, admin.Id, StringComparison.Ordinal))
            {
                return Result<ClinicUserDto>.Failure("You cannot deactivate your own account.");
            }

            var target = await _userRepository.GetByIdAsync(request.TargetUserId, cancellationToken);
            // Scope to the admin's own clinic.
            if (target == null || target.ClinicId != admin.ClinicId)
            {
                return Result<ClinicUserDto>.Failure("User not found");
            }

            if (request.IsActive)
            {
                target.Activate();
            }
            else
            {
                target.Deactivate();
            }

            _userRepository.Update(target);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<ClinicUserDto>.Success(new ClinicUserDto
            {
                Id = target.Id,
                ClinicId = target.ClinicId,
                Role = target.Role,
                Email = target.Email,
                FullName = target.FullName,
                IsActive = target.IsActive,
                MustChangePassword = target.MustChangePassword,
                LastLoginAt = target.LastLoginAt,
                CreatedAt = target.CreatedAt
            });
        }
        catch (Exception ex)
        {
            return Result<ClinicUserDto>.Failure($"Error updating user status: {ex.Message}");
        }
    }
}
