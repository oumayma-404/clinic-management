using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Users.Commands;

/// <summary>
/// Admin-only: creates a colleague's account directly, returning a one-time password to relay
/// (<c>multi-tenant-cloud</c> US-3).
///
/// <para><b>Why this had to exist.</b> Until now the only way a second person got an account was
/// <c>POST /api/auth/register</c> — self-registration behind the clinic's six-character join code. That is a
/// LAN-scale gate, so <c>HostedMultiTenant</c> closes it (<c>DeploymentProfile.AllowsSelfRegistration</c>), and
/// without this command a hosted clinic would have had <b>no way at all</b> to add staff. <c>UsersController</c>
/// exposed exactly <c>GET</c>, <c>{id}/reset-password</c>, <c>{id}/status</c> and <c>{id}/role</c>: every one of
/// them operates on an account somebody else had already created.</para>
///
/// <para>⚠️ <b>The account is created active, unlike <see cref="User.CreateSelfRegistered"/>.</b> The two differ in
/// who vouched for whom: a self-registration is a stranger asking to be let in, so it waits for approval (I5),
/// while this one <i>is</i> the approval — an admin typed the colleague's name. A pending account here would ask
/// the same admin to approve their own action.</para>
///
/// <para>⚠️ <b>The <c>doctor</c> role creates and links a <see cref="Doctor"/>, and that is not optional</b>
/// (review finding 4). It used to write only the <c>User</c> row, so on the one profile where this command is the
/// <i>only</i> way to add staff, every dentist added after <c>provision-clinic</c> had no practitioner record:
/// absent from the roster, nothing for « Mon profil » to edit, <c>PractitionerAttribution</c>'s caller fall-back
/// resolving to <c>null</c> so their invoices and fiches were unattributed, and — worst — <c>
/// PractitionerRenderSnapshot</c> finding no cachet and no n° d'ordre CNOMDT, so their certificats and ordonnances
/// printed with no practitioner identity at all. Both sibling paths (<c>JoinClinicCommand</c>,
/// <c>LocalClinicProvisioning</c>) already required the same information; this one now matches them.</para>
/// </summary>
public class CreateClinicUserCommand : IRequest<Result<CreatedClinicUserDto>>
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// The practitioner behind the account. <b>Required for the <c>doctor</c> role</b> and ignored for the other two,
    /// exactly as <c>JoinClinicCommand</c> treats it — an admin or a secretary is not a practitioner.
    /// </summary>
    public DoctorPersonalInfoDto? DoctorInfo { get; set; }
}

public class CreateClinicUserCommandHandler : IRequestHandler<CreateClinicUserCommand, Result<CreatedClinicUserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicContext _clinicContext;
    private readonly ILocalAuthService _localAuthService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateClinicUserCommandHandler> _logger;

    public CreateClinicUserCommandHandler(
        IUserRepository userRepository,
        IDoctorRepository doctorRepository,
        IClinicContext clinicContext,
        ILocalAuthService localAuthService,
        IUnitOfWork unitOfWork,
        ILogger<CreateClinicUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _doctorRepository = doctorRepository;
        _clinicContext = clinicContext;
        _localAuthService = localAuthService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<CreatedClinicUserDto>> Handle(CreateClinicUserCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<CreatedClinicUserDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            // The class policy is AdminOnly, but the DB role is the authoritative one everywhere in this codebase
            // — a JWT minted before a demotion still carries the old role until it expires.
            var admin = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin == null)
            {
                return Result<CreatedClinicUserDto>.Failure("Utilisateur introuvable.");
            }
            if (!admin.IsAdmin())
            {
                return Result<CreatedClinicUserDto>.Failure("Seuls les administrateurs peuvent créer un compte.");
            }

            var email = request.Email?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
            {
                return Result<CreatedClinicUserDto>.Failure("Un email valide est requis.");
            }
            if (string.IsNullOrWhiteSpace(request.FullName))
            {
                return Result<CreatedClinicUserDto>.Failure("Le nom complet est requis.");
            }

            var role = User.NormalizeRole(request.Role);
            if (role == null)
            {
                return Result<CreatedClinicUserDto>.Failure(
                    "Rôle invalide. Les rôles autorisés sont : admin, doctor, secretary.");
            }

            // Mirrors JoinClinicCommand: a doctor account with no practitioner behind it is what left every hosted
            // dentist without a cachet, a n° d'ordre or any attribution on the money they collected.
            if (role == User.RoleDoctor && !HasPractitioner(request.DoctorInfo))
            {
                return Result<CreatedClinicUserDto>.Failure(
                    "Le prénom, le nom et la spécialité du praticien sont requis pour le rôle « médecin ».");
            }

            // The partial unique index on the lowercased email would otherwise surface as a 500. It is checked
            // across every clinic deliberately — a local account is identified by its email alone at login, so a
            // second clinic reusing one would make the two indistinguishable to the one query that resolves them.
            // ⚠️ The refusal must NOT say the address is taken *elsewhere* (review finding 25): on a hosted backend
            // serving competing practices, a message that distinguishes « taken here » from « taken somewhere »
            // turns this endpoint into an oracle for « does this person hold an account on this service? », which
            // any clinic admin could walk against a list of addresses.
            var existing = await _userRepository.GetByEmailAsync(email, cancellationToken);
            if (existing != null)
            {
                return Result<CreatedClinicUserDto>.Failure(
                    existing.ClinicId == admin.ClinicId
                        ? "Un compte existe déjà avec cet email dans votre cabinet."
                        : "Cet email ne peut pas être utilisé pour un nouveau compte.");
            }

            var temporaryPassword = _localAuthService.GenerateTemporaryPassword();
            var user = User.CreateLocalUser(
                admin.ClinicId,
                role,
                email,
                _localAuthService.HashPassword(temporaryPassword),
                request.FullName.Trim(),
                mustChangePassword: true);

            await _userRepository.AddAsync(user, cancellationToken);

            if (role == User.RoleDoctor)
            {
                var doctor = new Doctor(
                    Guid.NewGuid(),
                    admin.ClinicId,
                    request.DoctorInfo!.FirstName,
                    request.DoctorInfo.LastName,
                    request.DoctorInfo.Specialty,
                    request.DoctorInfo.Phone,
                    user.Email);
                doctor.LinkToUser(user.Id);
                await _doctorRepository.AddAsync(doctor, cancellationToken);
            }

            // One save for both rows: an account whose practitioner record failed to commit is precisely the
            // half-created state this fix exists to remove.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<CreatedClinicUserDto>.Success(new CreatedClinicUserDto
            {
                UserId = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role,
                TemporaryPassword = temporaryPassword
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // A-8 defect class: never echo server internals back to a caller — this one would carry the email.
            _logger.LogError(ex, "Unhandled failure creating a clinic user");
            return Result<CreatedClinicUserDto>.Failure("Erreur lors de la création du compte. Veuillez réessayer.");
        }
    }

    /// <summary>
    /// The same three-field test <c>JoinClinicCommand</c> and <c>LocalClinicProvisioning</c> apply: a nameless or
    /// specialty-less practitioner is never persisted.
    /// </summary>
    private static bool HasPractitioner(DoctorPersonalInfoDto? info) =>
        info != null
        && !string.IsNullOrWhiteSpace(info.FirstName)
        && !string.IsNullOrWhiteSpace(info.LastName)
        && !string.IsNullOrWhiteSpace(info.Specialty);
}
