using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Commands;

public class CreateClinicCommand : IRequest<Result<ClinicDto>>
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool GenerateCode { get; set; } = true;
    public string Role { get; set; } = "doctor"; // "doctor" or "secretary"
    public DoctorPersonalInfoDto? DoctorInfo { get; set; } // Required if Role is "doctor"
    public List<DoctorDto>? Doctors { get; set; } // Legacy: additional doctors (not the creator)
    public Stream? LogoFile { get; set; } // Logo file stream
    public string? LogoContentType { get; set; } // Logo content type
}

public class CreateClinicCommandHandler : IRequestHandler<CreateClinicCommand, Result<ClinicDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IAuth0ManagementService _auth0ManagementService;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;

    public CreateClinicCommandHandler(
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IDoctorRepository doctorRepository,
        IClinicContext clinicContext,
        IAuth0ManagementService auth0ManagementService,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork)
    {
        _clinicRepository = clinicRepository;
        _userRepository = userRepository;
        _doctorRepository = doctorRepository;
        _clinicContext = clinicContext;
        _auth0ManagementService = auth0ManagementService;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ClinicDto>> Handle(CreateClinicCommand request, CancellationToken cancellationToken)
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

            // Get email from JWT claims
            var userEmail = _clinicContext.GetUserEmail();
            
            // Email should always be present in Auth0 JWT, but if not, we'll use the email from the request
            // This ensures the doctor always has an email
            if (string.IsNullOrWhiteSpace(userEmail) && !string.IsNullOrWhiteSpace(request.Email))
            {
                userEmail = request.Email;
            }

            // Generate clinic code if requested
            string? clinicCode = null;
            if (request.GenerateCode)
            {
                clinicCode = GenerateClinicCode();
                // Ensure code is unique
                while (await _clinicRepository.CodeExistsAsync(clinicCode, cancellationToken))
                {
                    clinicCode = GenerateClinicCode();
                }
            }

            // Create clinic first (we need the ID for logo path)
            var clinicId = Guid.NewGuid();
            var clinic = new Clinic(
                clinicId,
                request.Name,
                request.Address,
                request.Phone,
                request.Email,
                clinicCode);

            await _clinicRepository.AddAsync(clinic, cancellationToken);

            // Handle logo upload if provided
            if (request.LogoFile != null && !string.IsNullOrWhiteSpace(request.LogoContentType))
            {
                var logoPath = $"{clinicId}/logo";
                var logoUrl = await _fileStorage.UploadAsync(
                    request.LogoFile,
                    request.LogoContentType,
                    logoPath,
                    cancellationToken);
                
                // Update clinic with logo URL
                clinic.Update(clinic.Name, clinic.Address, clinic.Phone, clinic.Email, logoUrl);
            }

            // Determine user role: if doctor, use "doctor", otherwise use "secretary"
            // Note: The creator is not "admin" anymore, they have their selected role
            var userRole = role;

            // Create user and associate with clinic
            var user = new User(
                userId,
                clinic.Id,
                userRole,
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
                    userEmail);

                // Link doctor to user
                doctor.LinkToUser(userId);

                await _doctorRepository.AddAsync(doctor, cancellationToken);
            }

            // Create additional doctors if provided (legacy support)
            if (request.Doctors != null && request.Doctors.Any())
            {
                foreach (var doctorDto in request.Doctors)
                {
                    if (!string.IsNullOrWhiteSpace(doctorDto.Name) && !string.IsNullOrWhiteSpace(doctorDto.Specialty))
                    {
                        // Parse name into first and last name
                        var nameParts = doctorDto.Name.Split(' ', 2);
                        var firstName = nameParts[0];
                        var lastName = nameParts.Length > 1 ? nameParts[1] : "";

                        var doctor = new Doctor(
                            Guid.NewGuid(),
                            clinic.Id,
                            firstName,
                            lastName,
                            doctorDto.Specialty,
                            doctorDto.Phone,
                            doctorDto.Email);
                        await _doctorRepository.AddAsync(doctor, cancellationToken);
                    }
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Update Auth0 app_metadata
            try
            {
                await _auth0ManagementService.UpdateUserMetadataAsync(userId, clinic.Id, userRole, cancellationToken);
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
                LogoUrl = clinic.LogoUrl,
                CreatedAt = clinic.CreatedAt
            };

            return Result<ClinicDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<ClinicDto>.Failure($"Error creating clinic: {ex.Message}");
        }
    }

    private string GenerateClinicCode()
    {
        // Generate a 6-character alphanumeric code
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 6)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }
}

