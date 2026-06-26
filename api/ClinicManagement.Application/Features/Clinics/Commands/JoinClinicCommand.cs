using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Commands;

public class JoinClinicCommand : IRequest<Result<ClinicDto>>
{
    public string Code { get; set; } = string.Empty;
    public string Role { get; set; } = "secretary"; // "doctor" or "secretary"
    public DoctorPersonalInfoDto? DoctorInfo { get; set; } // Required if Role is "doctor"
}

public class JoinClinicCommandHandler : IRequestHandler<JoinClinicCommand, Result<ClinicDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IAuth0ManagementService _auth0ManagementService;
    private readonly IUnitOfWork _unitOfWork;

    public JoinClinicCommandHandler(
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IDoctorRepository doctorRepository,
        IClinicContext clinicContext,
        IAuth0ManagementService auth0ManagementService,
        IUnitOfWork unitOfWork)
    {
        _clinicRepository = clinicRepository;
        _userRepository = userRepository;
        _doctorRepository = doctorRepository;
        _clinicContext = clinicContext;
        _auth0ManagementService = auth0ManagementService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ClinicDto>> Handle(JoinClinicCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<ClinicDto>.Failure("User ID not found in token");
            }

            // Validate role
            var role = request.Role.ToLowerInvariant();
            if (role != "doctor" && role != "secretary")
            {
                return Result<ClinicDto>.Failure("Invalid role. Must be 'doctor' or 'secretary'");
            }

            // Validate doctor info if role is doctor
            if (role == "doctor")
            {
                if (request.DoctorInfo == null)
                {
                    return Result<ClinicDto>.Failure("Doctor personal information is required when role is 'doctor'");
                }

                if (string.IsNullOrWhiteSpace(request.DoctorInfo.FirstName) ||
                    string.IsNullOrWhiteSpace(request.DoctorInfo.LastName) ||
                    string.IsNullOrWhiteSpace(request.DoctorInfo.Specialty))
                {
                    return Result<ClinicDto>.Failure("First name, last name, and specialty are required for doctors");
                }
            }

            // Check if user already has a clinic
            var existingUser = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (existingUser != null)
            {
                return Result<ClinicDto>.Failure("User already belongs to a clinic");
            }

            // Find clinic by code
            var clinic = await _clinicRepository.GetByCodeAsync(request.Code, cancellationToken);
            if (clinic == null)
            {
                return Result<ClinicDto>.Failure("Invalid clinic code");
            }

            // Get email from JWT claims (same as CreateClinicCommand)
            // The email should be in the JWT token since it works for CreateClinicCommand
            var userEmail = _clinicContext.GetUserEmail();
            
            // Email should always be present in Auth0 JWT
            // If not found, this indicates a configuration issue, but we'll proceed
            // The email is optional in Doctor entity

            // Create user and associate with clinic
            var user = new User(
                userId,
                clinic.Id,
                role,
                userEmail,
                role == "doctor" && request.DoctorInfo != null
                    ? $"{request.DoctorInfo.FirstName} {request.DoctorInfo.LastName}".Trim()
                    : null);

            await _userRepository.AddAsync(user, cancellationToken);

            // Create doctor record if role is doctor
            if (role == "doctor" && request.DoctorInfo != null)
            {
                var doctor = new Doctor(
                    Guid.NewGuid(),
                    clinic.Id,
                    request.DoctorInfo.FirstName,
                    request.DoctorInfo.LastName,
                    request.DoctorInfo.Specialty,
                    request.DoctorInfo.Phone,
                    userEmail); // Email from authenticated user

                // Link doctor to user
                doctor.LinkToUser(userId);

                await _doctorRepository.AddAsync(doctor, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Update Auth0 app_metadata
            try
            {
                await _auth0ManagementService.UpdateUserMetadataAsync(userId, clinic.Id, role, cancellationToken);
            }
            catch (Exception)
            {
                // Log but don't fail the operation if Auth0 update fails
                // The user is already created in the database
                // TODO: Add proper logging
            }

            var dto = new ClinicDto
            {
                Id = clinic.Id,
                Name = clinic.Name,
                Address = clinic.Address,
                Phone = clinic.Phone,
                Email = clinic.Email,
                Code = clinic.Code,
                CreatedAt = clinic.CreatedAt
            };

            return Result<ClinicDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<ClinicDto>.Failure($"Error joining clinic: {ex.Message}");
        }
    }

}


