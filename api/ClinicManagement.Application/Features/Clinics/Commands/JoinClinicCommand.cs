using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
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

    // Local (offline) self-registration only. When Password is set, the handler creates a
    // local account from email+password using the clinic code. Never populated in Cloud mode.
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? FullName { get; set; }
}

public class JoinClinicCommandHandler : IRequestHandler<JoinClinicCommand, Result<ClinicDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IAuth0ManagementService _auth0ManagementService;
    private readonly ILocalAuthService _localAuthService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<JoinClinicCommandHandler> _logger;

    public JoinClinicCommandHandler(
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IDoctorRepository doctorRepository,
        IClinicContext clinicContext,
        IAuth0ManagementService auth0ManagementService,
        ILocalAuthService localAuthService,
        IUnitOfWork unitOfWork,
        ILogger<JoinClinicCommandHandler> logger)
    {
        _clinicRepository = clinicRepository;
        _userRepository = userRepository;
        _doctorRepository = doctorRepository;
        _clinicContext = clinicContext;
        _auth0ManagementService = auth0ManagementService;
        _localAuthService = localAuthService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ClinicDto>> Handle(JoinClinicCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Local (offline) self-registration: create a local account from email+password
            // using the clinic code. No authenticated user exists yet. Cloud path continues below.
            if (!string.IsNullOrEmpty(request.Password))
            {
                return await RegisterLocalUserAsync(request, cancellationToken);
            }

            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<ClinicDto>.Failure("Utilisateur introuvable dans le jeton d'authentification.");
            }

            // Validate role
            var role = request.Role.ToLowerInvariant();
            if (role != "doctor" && role != "secretary")
            {
                return Result<ClinicDto>.Failure("Rôle invalide. Choisissez « médecin » ou « secrétaire ».");
            }

            // Validate doctor info if role is doctor
            if (role == "doctor")
            {
                if (request.DoctorInfo == null)
                {
                    return Result<ClinicDto>.Failure("Les informations du praticien sont requises pour le rôle « médecin ».");
                }

                if (string.IsNullOrWhiteSpace(request.DoctorInfo.FirstName) ||
                    string.IsNullOrWhiteSpace(request.DoctorInfo.LastName) ||
                    string.IsNullOrWhiteSpace(request.DoctorInfo.Specialty))
                {
                    return Result<ClinicDto>.Failure("Le prénom, le nom et la spécialité sont requis pour un médecin.");
                }
            }

            // Check if user already has a clinic
            var existingUser = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (existingUser != null)
            {
                return Result<ClinicDto>.Failure("Cet utilisateur appartient déjà à un cabinet.");
            }

            // Find clinic by code
            var clinic = await _clinicRepository.GetByCodeAsync(request.Code, cancellationToken);
            if (clinic == null)
            {
                return Result<ClinicDto>.Failure("Code cabinet invalide.");
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
            // Detail to the log only — never leak infrastructure text to the join/register screen.
            _logger.LogError(ex, "Error joining clinic with code {Code}", request.Code);
            return Result<ClinicDto>.Failure(ErrorMessages.Generic);
        }
    }

    private async Task<Result<ClinicDto>> RegisterLocalUserAsync(JoinClinicCommand request, CancellationToken cancellationToken)
    {
        // Role: doctor/secretary only — admin is never self-assignable (AC-4.4).
        var role = request.Role.ToLowerInvariant();
        if (role != "doctor" && role != "secretary")
        {
            return Result<ClinicDto>.Failure("Rôle invalide. Choisissez « médecin » ou « secrétaire ».");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Result<ClinicDto>.Failure("L'email est requis.");
        }
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return Result<ClinicDto>.Failure("Le nom complet est requis.");
        }
        // FR-B2: password policy — minimum length.
        if (request.Password!.Length < PasswordPolicy.MinLength)
        {
            return Result<ClinicDto>.Failure($"Le mot de passe doit contenir au moins {PasswordPolicy.MinLength} caractères.");
        }

        if (role == "doctor")
        {
            if (request.DoctorInfo == null ||
                string.IsNullOrWhiteSpace(request.DoctorInfo.FirstName) ||
                string.IsNullOrWhiteSpace(request.DoctorInfo.LastName) ||
                string.IsNullOrWhiteSpace(request.DoctorInfo.Specialty))
            {
                return Result<ClinicDto>.Failure("Le prénom, le nom et la spécialité sont requis pour un médecin.");
            }
        }

        // AC-4.2: a valid clinic code is required.
        var clinic = await _clinicRepository.GetByCodeAsync(request.Code, cancellationToken);
        if (clinic == null)
        {
            return Result<ClinicDto>.Failure("Code cabinet invalide.");
        }

        // AC-4.3: email must be unique per install.
        var existing = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (existing != null)
        {
            return Result<ClinicDto>.Failure("Un compte existe déjà avec cet email.");
        }

        var passwordHash = _localAuthService.HashPassword(request.Password);
        var user = User.CreateLocalUser(clinic.Id, role, request.Email, passwordHash, request.FullName);
        await _userRepository.AddAsync(user, cancellationToken);

        if (role == "doctor" && request.DoctorInfo != null)
        {
            var doctor = new Doctor(
                Guid.NewGuid(),
                clinic.Id,
                request.DoctorInfo.FirstName,
                request.DoctorInfo.LastName,
                request.DoctorInfo.Specialty,
                request.DoctorInfo.Phone,
                user.Email); // normalized email, consistent with the account (matches User.CreateLocalUser)
            doctor.LinkToUser(user.Id);
            await _doctorRepository.AddAsync(doctor, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
}


