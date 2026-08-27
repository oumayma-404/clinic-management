using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// Local-mode login: authenticates an email + password against a local account
/// and returns an app-signed JWT. No-op / not used in Cloud mode.
/// </summary>
public class LoginCommand : IRequest<Result<LoginResultDto>>
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// The one-time code, where the account holds a second factor
    /// (<c>hosted-security-hardening</c> FR-1.1 – FR-1.2).
    ///
    /// <para>Absent is a real state and not an error: it is how the first request of a two-step sign-in looks,
    /// and it earns <c>totp_required</c> so the screen knows to ask.</para>
    /// </summary>
    public string? TotpCode { get; set; }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResultDto>>
{
    // The generic refusal — which never reveals whether the address exists — now comes from
    // `ClinicAuthRefusals` along with its code, so the sentence and the thing a client branches on cannot
    // drift apart. It also stops being the one English string on this path.

    // Same wording for both lockout tiers: the caller must not learn which brake stopped them.
    private const string LockedOutError =
        "Ce compte est temporairement bloqué après plusieurs tentatives de connexion échouées. Veuillez réessayer plus tard.";

    private readonly IUserRepository _userRepository;
    private readonly ILocalAuthService _localAuthService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILoginAttemptTracker _attemptTracker;
    private readonly ITotpService _totpService;
    private readonly ITotpReplayGuard _totpReplayGuard;
    private readonly IUserSecretProtector _secretProtector;
    private readonly ISecondFactorPolicy _secondFactorPolicy;
    private readonly ISessionFamilyRepository _sessionFamilies;
    private readonly IAuditActorProvider _auditActor;

    public LoginCommandHandler(
        IUserRepository userRepository,
        ILocalAuthService localAuthService,
        IUnitOfWork unitOfWork,
        ILoginAttemptTracker attemptTracker,
        ITotpService totpService,
        ITotpReplayGuard totpReplayGuard,
        IUserSecretProtector secretProtector,
        ISecondFactorPolicy secondFactorPolicy,
        ISessionFamilyRepository sessionFamilies,
        IAuditActorProvider auditActor)
    {
        _userRepository = userRepository;
        _localAuthService = localAuthService;
        _unitOfWork = unitOfWork;
        _attemptTracker = attemptTracker;
        _totpService = totpService;
        _totpReplayGuard = totpReplayGuard;
        _secretProtector = secretProtector;
        _secondFactorPolicy = secondFactorPolicy;
        _sessionFamilies = sessionFamilies;
        _auditActor = auditActor;
    }

    /// <summary>
    /// Must this account present a code to sign in?
    ///
    /// <para>Two independent grounds, and the second is what makes voluntary enrolment meaningful: the
    /// deployment requires it <b>of administrators</b>, or the account has enrolled one of its own accord. A
    /// doctor who enrolled voluntarily is asked for their code on every deployment — offering it and then not
    /// checking it would be worse than never offering it.</para>
    /// </summary>
    private bool SecondFactorApplies(Domain.Entities.User user) =>
        user.IsTotpEnrolled || (_secondFactorPolicy.RequiresAdminSecondFactor && user.IsAdmin());

    private static Result<LoginResultDto> Refuse(string code) =>
        Result<LoginResultDto>.Failure(ClinicAuthRefusals.MessageFor(code)!, code);

    public async Task<Result<LoginResultDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Refuse(ClinicAuthRefusals.InvalidCredentials);
            }

            var user = await _userRepository.GetByEmailAsync(request.Email.Trim(), cancellationToken);

            // No such account, or a Cloud (Auth0) account with no local password.
            if (user == null || !user.IsLocalAccount())
            {
                return Refuse(ClinicAuthRefusals.InvalidCredentials);
            }

            // Both lockouts are checked before the password so a brute-force attempt is actually
            // stopped (AC-3.4) — this necessarily discloses the locked state, an accepted
            // trade-off. The deactivated state, by contrast, is disclosed only after a correct
            // password (below) so it can't be used to enumerate accounts.
            //
            // Primary brake: this source has burned its attempts against this account (AC-4.2). Only the
            // offending machine is refused — a colleague on another PC signs in normally, which is the whole
            // point: the previous account-only lockout let one hostile host lock the entire clinic out.
            if (_attemptTracker.IsLockedOutForCurrentSource(user.Id))
            {
                return Result<LoginResultDto>.Failure(LockedOutError, ClinicAuthRefusals.TooManyAttempts);
            }

            // Durable cross-source backstop (AC-4.3), at a threshold no single source can reach alone. Also
            // what survives the restart that clears the in-memory per-source counters.
            if (user.IsLockedOut())
            {
                return Result<LoginResultDto>.Failure(LockedOutError, ClinicAuthRefusals.TooManyAttempts);
            }

            var outcome = _localAuthService.VerifyPassword(user.PasswordHash!, request.Password);
            if (outcome == PasswordVerificationOutcome.Failed)
            {
                _attemptTracker.RecordFailure(user.Id);
                user.RecordFailedLogin();
                _userRepository.Update(user);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Refuse(ClinicAuthRefusals.InvalidCredentials);
            }

            // Disclosed only to a caller who supplied the correct password (the account owner).
            //
            // The two inactive states read differently to the person in front of the screen, and telling them
            // apart is the whole point of I5's pending state: someone who registered ten seconds ago has done
            // nothing wrong and needs to know an approval is coming, while « désactivé » on a freshly-created
            // account reads as a bug in the registration they just completed. Both messages point at the same
            // person; only one of them is an accusation.
            if (!user.IsActive)
            {
                return Result<LoginResultDto>.Failure(
                    user.IsPendingActivation
                        ? "Votre compte a bien été créé mais doit encore être activé par un administrateur du cabinet. Vous pourrez vous connecter dès qu'il l'aura fait."
                        : "Ce compte a été désactivé. Veuillez contacter l'administrateur de votre cabinet.",
                    ClinicAuthRefusals.AccountDisabled);
            }

            // ── The second factor (hosted-security-hardening FR-1.1 – FR-1.2) ──────────────────────────────
            //
            // Placed here, after the password and after the active check, in PlatformLoginCommand's own order.
            // Before the password it would be an oracle: « ce compte doit enrôler » to anyone who guesses an
            // address tells them the account exists and is an administrator.
            if (SecondFactorApplies(user))
            {
                // Required of this account but never set up. 403, carrying nothing else — the client routes to
                // the enrolment step, which re-presents the password it already has.
                if (!user.IsTotpEnrolled)
                {
                    return Refuse(ClinicAuthRefusals.TotpEnrolmentRequired);
                }

                // No code offered yet: the ordinary first half of a two-step sign-in, not a failure. It spends
                // no attempt — the password was correct, and the user has simply not been asked yet.
                if (string.IsNullOrWhiteSpace(request.TotpCode))
                {
                    return Refuse(ClinicAuthRefusals.TotpRequired);
                }

                // ⚠️ An undecryptable secret REFUSES and is logged; it never falls through to « no factor
                // required ». The key ring is the only thing that can cause it, the recovery is
                // `reset-user-totp`, and the alternative degradation would silently disarm the whole feature
                // for every administrator at once.
                /*
                 * ⚠️ Verified FIRST, then claimed — and the order is the whole of the replay fix.
                 *
                 * RFC 6238 § 5.2 forbids accepting the second presentation of a code, and this product makes the
                 * factor mandatory for administrators, so a code observed once was replayable for the rest of its
                 * ~90-second accepted window. Claiming BEFORE verifying would be worse than not guarding at all:
                 * a wrong guess would burn the real code's one use and lock the account's own owner out of their
                 * own window.
                 *
                 * A replay lands in the SAME branch as a wrong code — same counter, same `invalid_credentials` —
                 * so it cannot be used to learn that the code was otherwise valid, which is what would turn the
                 * guard into an oracle.
                 */
                if (!_secretProtector.TryUnprotect(user.ProtectedTotpSecret!, out var secret)
                    || !_totpService.VerifyCode(secret, request.TotpCode!)
                    || !_totpReplayGuard.TryConsume(user.Id, request.TotpCode!))
                {
                    // A present-but-wrong code is deliberately indistinguishable from a wrong password: it
                    // spends an attempt and answers `invalid_credentials`, so the ladder cannot be used to
                    // learn which half was right.
                    _attemptTracker.RecordFailure(user.Id);
                    user.RecordFailedLogin();
                    _userRepository.Update(user);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    return Refuse(ClinicAuthRefusals.InvalidCredentials);
                }
            }

            // The stored hash used an outdated format — upgrade it now that we have the plaintext.
            if (outcome == PasswordVerificationOutcome.SuccessNeedsRehash)
            {
                user.UpgradePasswordHash(_localAuthService.HashPassword(request.Password));
            }

            // A user who simply mistyped should not carry a penalty into their next session.
            _attemptTracker.ClearForCurrentSource(user.Id);
            /*
             * ⚠️ Declared BEFORE the save that stamps `LastLoginAt`, or the row it writes is attributed to
             * `job|unknown` and the journal renders « Tâche automatique » for a person signing in — 329 of 1 868
             * rows, ~18 %, on the one ledger an owner reaches for when something else has gone wrong. This
             * endpoint is anonymous by construction (the token is its output), so nothing else can know who this is.
             */
            _auditActor.AuthenticatedAs(user.Id, user.Email);
            user.RecordSuccessfulLogin();
            var token = _localAuthService.GenerateToken(user);

            // ── The durable session credential, and the chain it belongs to ───────────────────────────────
            //
            // Both are staged BEFORE the single save, so the account's login stamp and its new session family
            // land in one transaction. A second save here would leave a window in which the user is recorded
            // as signed in with no family to rotate — and it would break the one-save invariant this handler's
            // own test asserts.
            //
            // FR-1.6: the credential names its own family, so a later exchange can tell « the one I issued »
            // from « one I replaced three rotations ago ».
            var family = new SessionFamily(
                user.Id,
                // A placeholder, replaced two lines down: the family needs an id before the token can name it,
                // and the token must exist before it can be hashed into the family. Deliberately an already-
                // expired instant, so a row that somehow escaped the rotation is purged rather than trusted.
                SessionCredential.Hash(Guid.NewGuid().ToString()),
                DateTime.UtcNow);
            await _sessionFamilies.AddAsync(family, cancellationToken);

            var refreshToken = _localAuthService.GenerateRefreshToken(user, family.Id);
            family.Rotate(SessionCredential.Hash(refreshToken.AccessToken), refreshToken.ExpiresAtUtc);

            _userRepository.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var result = new LoginResultDto
            {
                AccessToken = token.AccessToken,
                RefreshToken = refreshToken.AccessToken,
                ExpiresAt = token.ExpiresAtUtc,
                RefreshExpiresAt = refreshToken.ExpiresAtUtc,
                MustChangePassword = user.MustChangePassword,
                User = new UserDto
                {
                    Id = user.Id,
                    ClinicId = user.ClinicId,
                    Role = user.Role,
                    Email = user.Email,
                    FullName = user.FullName,
                    CreatedAt = user.CreatedAt
                }
            };

            return Result<LoginResultDto>.Success(result);
        }
        catch (Exception)
        {
            // Anonymous endpoint: do not echo internal exception details to the caller.
            return Result<LoginResultDto>.Failure("Une erreur inattendue est survenue lors de la connexion. Veuillez réessayer.");
        }
    }
}
