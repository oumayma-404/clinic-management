using MediatR;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.ProcedureTypes;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Commands;

public class CreateClinicCommand : IRequest<Result<ClinicDto>>
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool GenerateCode { get; set; } = true;
    public string Role { get; set; } = "doctor"; // "doctor" or "secretary"
    public DoctorPersonalInfoDto? DoctorInfo { get; set; } // Required if Role is "doctor"
    public List<DoctorDto>? Doctors { get; set; } // Legacy: additional doctors (not the creator)
    public Stream? LogoFile { get; set; } // Logo file stream
    public string? LogoContentType { get; set; } // Logo content type

    // Local (offline) first-run only. When Password is set, the handler creates the clinic +
    // first admin from email+password (no Auth0). Never populated in Cloud mode.
    public string? Password { get; set; }
    public string? FullName { get; set; } // Admin's full name (Local first-run)
}

public class CreateClinicCommandHandler : IRequestHandler<CreateClinicCommand, Result<ClinicDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IAuth0ManagementService _auth0ManagementService;
    private readonly IFileStorage _fileStorage;
    private readonly ILocalAuthService _localAuthService;
    private readonly IClinicCatalogSeeder _clinicCatalogSeeder;
    private readonly IUnitOfWork _unitOfWork;

    public CreateClinicCommandHandler(
        IClinicRepository clinicRepository,
        IProcedureTypeRepository procedureTypeRepository,
        IUserRepository userRepository,
        IDoctorRepository doctorRepository,
        IClinicContext clinicContext,
        IAuth0ManagementService auth0ManagementService,
        IFileStorage fileStorage,
        ILocalAuthService localAuthService,
        IClinicCatalogSeeder clinicCatalogSeeder,
        IUnitOfWork unitOfWork)
    {
        _clinicRepository = clinicRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _userRepository = userRepository;
        _doctorRepository = doctorRepository;
        _clinicContext = clinicContext;
        _auth0ManagementService = auth0ManagementService;
        _fileStorage = fileStorage;
        _localAuthService = localAuthService;
        _clinicCatalogSeeder = clinicCatalogSeeder;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ClinicDto>> Handle(CreateClinicCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Local (offline) first-run: create the clinic + first admin from email+password.
            // No authenticated user exists yet (this is the bootstrap), so this path never
            // reads the JWT/clinic context. Cloud mode continues below, unchanged.
            if (!string.IsNullOrEmpty(request.Password))
            {
                return await CreateLocalFirstRunAsync(request, cancellationToken);
            }

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
                clinicCode = ClinicCodeGenerator.Generate();
                // Ensure code is unique
                while (await _clinicRepository.CodeExistsAsync(clinicCode, cancellationToken))
                {
                    clinicCode = ClinicCodeGenerator.Generate();
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
                clinicCode,
                request.City);

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
                
                // Update clinic with logo URL (preserve the city already set on the clinic)
                clinic.Update(clinic.Name, clinic.Address, clinic.Phone, clinic.Email, logoUrl, clinic.City);
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

            // Seed the clinic's procedure menu with the common Tunisian dental procedures (all editable).
            await SeedDefaultProcedureTypesAsync(clinic.Id, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Seed the clinic's reference catalogs (CNAM / medications / dental acts) with the shared default (#5).
            await SeedClinicCatalogsAsync(clinic.Id, cancellationToken);

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
                City = clinic.City,
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

    private async Task<Result<ClinicDto>> CreateLocalFirstRunAsync(CreateClinicCommand request, CancellationToken cancellationToken)
    {
        // AC-1.2a: setup is a one-time bootstrap — closed once any user exists.
        if (await _userRepository.AnyUserExistsAsync(cancellationToken))
        {
            return Result<ClinicDto>.Failure("Setup has already been completed for this installation.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<ClinicDto>.Failure("Clinic name is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Result<ClinicDto>.Failure("Email is required.");
        }
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return Result<ClinicDto>.Failure("Full name is required.");
        }
        // FR-B2: password policy — minimum length (enforced at the API).
        if (request.Password!.Length < PasswordPolicy.MinLength)
        {
            return Result<ClinicDto>.Failure($"Password must be at least {PasswordPolicy.MinLength} characters.");
        }

        // Single-dentist cabinet: when DoctorInfo is supplied the admin is also the practitioner, so a full
        // name is required (mirrors the Cloud CreateClinic + JoinClinic doctor paths) — never persist a
        // nameless Doctor. Absent DoctorInfo → an admin-only account, no practitioner validation.
        if (request.DoctorInfo != null && !string.IsNullOrWhiteSpace(request.DoctorInfo.Specialty)
            && (string.IsNullOrWhiteSpace(request.DoctorInfo.FirstName) || string.IsNullOrWhiteSpace(request.DoctorInfo.LastName)))
        {
            return Result<ClinicDto>.Failure("First and last name are required for the practitioner.");
        }

        // Generate a unique clinic code for later staff self-registration.
        var code = ClinicCodeGenerator.Generate();
        while (await _clinicRepository.CodeExistsAsync(code, cancellationToken))
        {
            code = ClinicCodeGenerator.Generate();
        }

        var clinic = new Clinic(
            Guid.NewGuid(),
            request.Name,
            request.Address,
            request.Phone,
            request.Email,
            code,
            request.City);
        await _clinicRepository.AddAsync(clinic, cancellationToken);

        var passwordHash = _localAuthService.HashPassword(request.Password);
        var admin = User.CreateLocalUser(clinic.Id, "admin", request.Email, passwordHash, request.FullName);
        await _userRepository.AddAsync(admin, cancellationToken);

        // Single-dentist cabinet: when the first admin is also the practitioner, create + link a Doctor so
        // their document identity (cachet, CNOMDT ordre) and "Mon profil" work. The admin keeps the "admin"
        // role; the linked Doctor is what the practitioner pages resolve by user id. Absent DoctorInfo → an
        // admin-only account (e.g. a non-clinical office manager), unchanged.
        if (request.DoctorInfo != null && !string.IsNullOrWhiteSpace(request.DoctorInfo.Specialty))
        {
            var doctor = new Doctor(
                Guid.NewGuid(),
                clinic.Id,
                request.DoctorInfo.FirstName,
                request.DoctorInfo.LastName,
                request.DoctorInfo.Specialty,
                request.DoctorInfo.Phone,
                request.Email);
            doctor.LinkToUser(admin.Id);
            await _doctorRepository.AddAsync(doctor, cancellationToken);
        }

        // Seed the clinic's procedure menu with the common Tunisian dental procedures (all editable).
        await SeedDefaultProcedureTypesAsync(clinic.Id, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Seed the clinic's reference catalogs (CNAM / medications / dental acts) with the shared default (#5).
        await SeedClinicCatalogsAsync(clinic.Id, cancellationToken);

        return Result<ClinicDto>.Success(new ClinicDto
        {
            Id = clinic.Id,
            Name = clinic.Name,
            Address = clinic.Address,
            Phone = clinic.Phone,
            Email = clinic.Email,
            Code = clinic.Code,
            LogoUrl = clinic.LogoUrl,
            CreatedAt = clinic.CreatedAt
        });
    }

    private async Task SeedDefaultProcedureTypesAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        foreach (var procedureType in ProcedureTypeCatalogSeed.CreateFor(clinicId))
        {
            await _procedureTypeRepository.AddAsync(procedureType, cancellationToken);
        }
    }

    // Best-effort (#5): seed the clinic's reference catalogs after it is committed. A failure here must not
    // undo the already-created clinic — the startup backfill (IClinicCatalogSeeder.SeedAllClinicsAsync)
    // re-seeds any clinic that is missing a catalog on the next boot.
    private async Task SeedClinicCatalogsAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        try
        {
            await _clinicCatalogSeeder.SeedForClinicAsync(clinicId, cancellationToken);
        }
        catch
        {
            // Swallowed: the startup backfill is the safety net (see SeedAllClinicsAsync).
        }
    }
}

