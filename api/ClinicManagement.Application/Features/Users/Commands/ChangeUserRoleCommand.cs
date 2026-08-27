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
/// Admin-only: move a clinic user between <c>admin</c>, <c>doctor</c> and <c>secretary</c> (AC-P2.23).
/// <para>
/// No user's role could ever be changed before — a member onboarded as a secretary who turned out to be the
/// practitioner had to be deactivated and re-registered under a new account, orphaning everything already
/// recorded against the old one.
/// </para>
/// </summary>
public class ChangeUserRoleCommand : IRequest<Result<ClinicUserDto>>
{
    public string TargetUserId { get; set; } = string.Empty;
    /// <summary>One of <see cref="User.AssignableRoles"/>, case-insensitive.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the user was
    /// editing; <c>0</c> means « not supplied » and skips the check (see <c>IUnitOfWork.SetExpectedVersion</c>).
    /// </summary>
    public uint Version { get; set; }
}

public class ChangeUserRoleCommandHandler : IRequestHandler<ChangeUserRoleCommand, Result<ClinicUserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ChangeUserRoleCommandHandler> _logger;

    public ChangeUserRoleCommandHandler(
        IUserRepository userRepository,
        IDoctorRepository doctorRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        ILogger<ChangeUserRoleCommandHandler> logger)
    {
        _userRepository = userRepository;
        _doctorRepository = doctorRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ClinicUserDto>> Handle(ChangeUserRoleCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<ClinicUserDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var admin = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin == null)
            {
                return Result<ClinicUserDto>.Failure("Utilisateur introuvable.");
            }

            // The controller is AdminOnly, but the authoritative check is the DB role — the same
            // defense-in-depth every other admin command applies rather than trusting the JWT claim.
            if (!admin.IsAdmin())
            {
                return Result<ClinicUserDto>.Failure("Seuls les administrateurs peuvent modifier le rôle d'un utilisateur.");
            }

            if (string.IsNullOrWhiteSpace(request.TargetUserId))
            {
                return Result<ClinicUserDto>.Failure("L'utilisateur cible est requis.");
            }

            // AC-P2.24 (A-11): validated against the closed set before anything is loaded or mutated. An
            // unvalidated role matches no authorization policy, so the account would silently lose every
            // surface — a "successful" call that quietly breaks the user.
            var role = User.NormalizeRole(request.Role);
            if (role == null)
            {
                return Result<ClinicUserDto>.Failure(
                    "Rôle invalide. Choisissez « admin », « doctor » ou « secretary ».");
            }

            var target = await _userRepository.GetByIdAsync(request.TargetUserId, cancellationToken);
            // Scope to the admin's own clinic (tenant isolation): another clinic's user reads as not found.
            if (target == null || target.ClinicId != admin.ClinicId)
            {
                return Result<ClinicUserDto>.Failure("Utilisateur introuvable.");
            }

            // AC-P2.26: the self-lockout guard, extended from SetUserActiveCommand's deactivation case. An
            // admin demoting themselves is legitimate as long as someone else can still administer the clinic;
            // doing it as the ONLY active admin leaves nobody able to manage users, and the offline recovery
            // utility resets a password — it does not grant a role.
            var demotingSelf = string.Equals(request.TargetUserId, admin.Id, StringComparison.Ordinal)
                && admin.IsAdmin()
                && role != User.RoleAdmin;
            if (demotingSelf)
            {
                // Unpaged: the guard below asks whether ANY other active admin exists, which a page cannot answer.
                var clinicUsers = (await _userRepository.GetByClinicIdAsync(
                    admin.ClinicId, cancellationToken: cancellationToken)).Items;
                var otherActiveAdmins = clinicUsers.Count(u =>
                    u.IsAdmin()
                    && u.IsActive
                    && !string.Equals(u.Id, admin.Id, StringComparison.Ordinal));
                if (otherActiveAdmins == 0)
                {
                    return Result<ClinicUserDto>.Failure(
                        "Vous êtes le seul administrateur actif du cabinet : nommez d'abord un autre administrateur.");
                }
            }

            // Returns false when the account already holds that role — do not bump TokenVersion (and log the
            // user out) for a re-selection that changes nothing.
            if (target.ChangeRole(role))
            {
                /*
                 * ⚠️ Promoting to « Médecin » must LINK a practitioner record, exactly as the create path does.
                 *
                 * It did not, and nothing on any screen said so: a promoted dentist had no `Doctors` row, so their
                 * ordonnances printed no cachet and no n° CNOMDT, they could not be picked as the praticien of a
                 * séance, and the money and clinical work they did were attributed to nobody — `L9`'s whole
                 * per-practitioner half silently excluded them. `CreateClinicUserCommand` calls this obligation
                 * « not optional » in its own docstring; the second door onto the same state skipped it.
                 *
                 * The name is split from the account's own `FullName` rather than asked for: this command has no
                 * form behind it (it is a rôle Select on a row), and an unnamed practitioner record is still
                 * strictly better than none — « Mon profil » is where the dentist completes it. Specialty is left
                 * to the same screen.
                 */
                if (role == User.RoleDoctor
                    && await _doctorRepository.GetByUserIdAsync(target.Id, cancellationToken) is null)
                {
                    var (firstName, lastName) = SplitName(target.FullName, target.Email);
                    var doctor = new Doctor(
                        Guid.NewGuid(),
                        target.ClinicId,
                        firstName,
                        lastName,
                        DefaultSpecialty,
                        phone: null,
                        email: target.Email);
                    doctor.LinkToUser(target.Id);
                    await _doctorRepository.AddAsync(doctor, cancellationToken);
                }

                // Band B — two admins on the users screen at once must not silently overwrite each other's choice.
                _unitOfWork.SetExpectedVersion(target, request.Version);
                _userRepository.Update(target);
                // One save for both rows, like the create path: an account promoted with no practitioner record
                // committed is the half-created state this exists to remove.
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<ClinicUserDto>.Success(target.ToClinicUserDto());
        }
        catch (ArgumentException ex)
        {
            // ChangeRole's own guard. Reachable only if the NormalizeRole check above were ever bypassed, but
            // the message is already French and caller-safe.
            return Result<ClinicUserDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Unhandled failure changing the role of user {TargetUserId}", request.TargetUserId);
            return Result<ClinicUserDto>.Failure("Erreur lors de la modification du rôle. Veuillez réessayer.");
        }
    }

    /// <summary>
    /// What a promoted practitioner's record is named, from the account's own <c>FullName</c>.
    ///
    /// <para>The last word is the surname and everything before it the given name(s) — the order this product
    /// stores a full name in. A single word becomes the surname with an empty given name rather than the reverse:
    /// a `Doctor` is addressed as « Dr {LastName} » throughout, so that is the half that must not be blank. With no
    /// name at all the address stands in, because an empty practitioner is unpickable in the séance form.</para>
    /// </summary>
    private static (string FirstName, string LastName) SplitName(string? fullName, string? email)
    {
        var parts = (fullName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            0 => (string.Empty, email?.Trim() is { Length: > 0 } address ? address : "Praticien"),
            1 => (string.Empty, parts[0]),
            _ => (string.Join(' ', parts[..^1]), parts[^1]),
        };
    }

    /// <summary>
    /// The specialty a promotion assigns. Deliberately the generic one and not a guess: this command has no form
    /// behind it, and « Mon profil » is where the dentist states their own.
    /// </summary>
    private const string DefaultSpecialty = "Dentiste";
}
