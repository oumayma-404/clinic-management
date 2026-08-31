using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// What an enrolment step answers with. Exactly one half is populated per call.
/// </summary>
/// <param name="SecretUri">The <c>otpauth://</c> URI to render as a QR — step one only.</param>
/// <param name="Secret">The same secret in readable groups, for typing it in by hand — step one only.</param>
/// <param name="SecretQrPng">
/// The same URI as a <c>data:image/png;base64,…</c> QR — step one only.
///
/// <para>⚠️ <b>Rendered server-side and inlined, rather than served from an image endpoint or drawn in the
/// browser.</b> An <c>&lt;img src="/api/…"&gt;</c> cannot carry a credential here — the caller has no session
/// yet, which is the whole reason this flow exists — and a client-side renderer would be a new runtime
/// dependency in a project with no test runner or CI to vet it. A data URI is not an image *request*: it
/// travels inside this response, which already carries the secret in two other fields, so it widens nothing.
/// The generator is the one already live for the LAN trust page.</para>
/// </param>
/// <param name="RecoveryCodes">The eight codes, shown <b>once</b> — step two only.</param>
public record TotpEnrolmentDto(
    string? SecretUri,
    string? Secret,
    string? SecretQrPng,
    IReadOnlyList<string>? RecoveryCodes);

/// <summary>
/// Enrols a clinic account's second factor from the login screen itself
/// (<c>hosted-security-hardening</c> FR-1.3).
///
/// <para><b>Two calls, one command.</b> The first supplies the address and password and gets a secret to scan;
/// the second re-supplies them with a code generated from it. It is one command because it is one decision — may
/// this caller enrol this account? — and answering that twice in two handlers is how the second copy ends up
/// checking the password less carefully than the first.</para>
///
/// <para>⚠️ <b>The password is verified before anything is minted.</b> Without that, knowing an address would be
/// enough to overwrite a colleague's enrolment: <c>IssueTotpSecret</c> clears the previous secret and every
/// recovery code, so an unauthenticated call to step one would be a denial-of-service on their sign-in.</para>
///
/// <para>⚠️ <b>The recovery codes are minted only when the code verifies</b> — step one persists an
/// <i>unconfirmed</i> secret and nothing else, which is precisely the state
/// <c>TotpEnrolledAt is null &amp;&amp; ProtectedTotpSecret is not null</c> exists to represent.</para>
///
/// <para>⚠️ <b>It issues no session</b>, deliberately: enrolling a factor is not signing in. The screen stops on
/// the recovery codes and the user then signs in normally with their new code — which also proves the
/// authenticator works before they are relying on it.</para>
///
/// <para>⚠️ <b>Step one leaves the account momentarily un-enrolled, and that is pre-existing rather than new.</b>
/// <c>IssueTotpSecret</c> clears the confirmed secret and every recovery code, so an abandoned replacement leaves
/// the account in the same state an administrator's reset leaves it in: whoever next presents the password can
/// complete an enrolment. The exposure is not widened by the replacement grant, because reaching that state
/// still requires a grant, a reset or the vendor's verb — a caller holding the password alone is refused above.
/// Narrowing it further means holding the new secret <i>pending</i> beside the old one until step two confirms,
/// which would change <c>IssueTotpSecret</c>'s contract for the reset paths too, and is not attempted here.</para>
/// </summary>
public class EnrolTotpCommand : IRequest<Result<TotpEnrolmentDto>>
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Absent on step one (« give me a secret »), present on step two (« here is the proof »).</summary>
    public string? TotpCode { get; set; }
}

public class EnrolTotpCommandHandler : IRequestHandler<EnrolTotpCommand, Result<TotpEnrolmentDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly ILocalAuthService _localAuthService;
    private readonly ITotpService _totpService;
    private readonly IUserSecretProtector _secretProtector;
    private readonly IQrCodeGenerator _qrCodeGenerator;
    private readonly ILoginAttemptTracker _attemptTracker;
    private readonly IUnitOfWork _unitOfWork;

    public EnrolTotpCommandHandler(
        IUserRepository userRepository,
        IClinicRepository clinicRepository,
        ILocalAuthService localAuthService,
        ITotpService totpService,
        IUserSecretProtector secretProtector,
        IQrCodeGenerator qrCodeGenerator,
        ILoginAttemptTracker attemptTracker,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _clinicRepository = clinicRepository;
        _localAuthService = localAuthService;
        _totpService = totpService;
        _secretProtector = secretProtector;
        _qrCodeGenerator = qrCodeGenerator;
        _attemptTracker = attemptTracker;
        _unitOfWork = unitOfWork;
    }

    private static Result<TotpEnrolmentDto> Refuse(string code) =>
        Result<TotpEnrolmentDto>.Failure(ClinicAuthRefusals.MessageFor(code)!, code);

    public async Task<Result<TotpEnrolmentDto>> Handle(
        EnrolTotpCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Refuse(ClinicAuthRefusals.InvalidCredentials);
            }

            var user = await _userRepository.GetByEmailAsync(request.Email.Trim(), cancellationToken);
            if (user is null || !user.IsLocalAccount())
            {
                return Refuse(ClinicAuthRefusals.InvalidCredentials);
            }

            // Both lockout tiers, before the password — `RedeemRecoveryCodeCommand`'s rule, and its reason
            // verbatim: « or this endpoint would be the unrated door beside a rate-limited one ».
            //
            // ⚠️ That reasoning was written one file over and never reached here, which left this endpoint an
            // unauthenticated password oracle: it verifies a password, branches distinguishably on the result
            // (wrong → `invalid_credentials`, right-but-enrolled → `totp_already_enrolled`, right-and-not →
            // 200 with a fresh secret), and recorded no failure anywhere. Neither the per-(account, source)
            // lockout nor the durable 50-attempt one applied, so the only brake was a sliding 30-per-5-minutes
            // that never trips a lockout at all — roughly 8 600 guesses a day against one account, for ever.
            if (_attemptTracker.IsLockedOutForCurrentSource(user.Id) || user.IsLockedOut())
            {
                return Result<TotpEnrolmentDto>.Failure(
                    "Ce compte est temporairement bloqué après plusieurs tentatives de connexion échouées. Veuillez réessayer plus tard.",
                    ClinicAuthRefusals.TooManyAttempts);
            }

            // Nothing is minted, cleared or persisted before this passes.
            if (_localAuthService.VerifyPassword(user.PasswordHash!, request.Password)
                == PasswordVerificationOutcome.Failed)
            {
                _attemptTracker.RecordFailure(user.Id);
                user.RecordFailedLogin();
                _userRepository.Update(user);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Refuse(ClinicAuthRefusals.InvalidCredentials);
            }

            // A second enrolment is refused rather than silently replacing the first, because letting the
            // password alone do it would make the second factor worth exactly as much as the password.
            //
            // ⚠️ **Unless the account holds a live replacement grant**, which only a redeemed recovery code
            // opens — i.e. the caller has already presented a second factor for this very purpose. Without that
            // exception the refusal was absolute, and re-securing a lost factor was an administrator's or the
            // vendor's action *only*: a cabinet whose single admin lost their phone could sign in with each of
            // their eight codes and never once move the factor to the new one. The grant is what distinguishes
            // « somebody knows this password » from « the owner is standing here holding their recovery codes ».
            if (user.IsTotpEnrolled && !user.IsTotpReplacementGranted())
            {
                return Refuse(ClinicAuthRefusals.TotpAlreadyEnrolled);
            }

            // ── Step one: issue an unconfirmed secret and hand back something to scan ──────────────────────
            if (string.IsNullOrWhiteSpace(request.TotpCode))
            {
                var secret = _totpService.GenerateSecret();
                user.IssueTotpSecret(_secretProtector.Protect(secret));
                _userRepository.Update(user);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // The practice's name, so somebody working at two of them can tell the entries apart.
                var clinic = await _clinicRepository.GetByIdAsync(user.ClinicId, cancellationToken);

                var uri = TotpEnrolmentUri.Build(
                    clinic?.Name ?? string.Empty, user.Email ?? string.Empty, secret);

                // A failed render must not cost the enrolment: the readable secret below is a complete way in,
                // and the screen shows an explicit retry rather than an empty box.
                string? qr = null;
                try
                {
                    qr = "data:image/png;base64," + Convert.ToBase64String(_qrCodeGenerator.GeneratePng(uri));
                }
                catch
                {
                    // Left null — see above.
                }

                return Result<TotpEnrolmentDto>.Success(new TotpEnrolmentDto(
                    SecretUri: uri,
                    Secret: TotpEnrolmentUri.ForReading(secret),
                    SecretQrPng: qr,
                    RecoveryCodes: null));
            }

            // ── Step two: confirm it, and only now mint the recovery codes ────────────────────────────────
            if (string.IsNullOrEmpty(user.ProtectedTotpSecret))
            {
                // Step two reached without step one — the client lost its place, or the row was reset under it.
                return Refuse(ClinicAuthRefusals.TotpEnrolmentRequired);
            }

            if (!_secretProtector.TryUnprotect(user.ProtectedTotpSecret, out var storedSecret)
                || !_totpService.VerifyCode(storedSecret, request.TotpCode!))
            {
                return Refuse(ClinicAuthRefusals.TotpInvalid);
            }

            var codes = Enumerable
                .Range(0, UserRecoveryCode.CountPerEnrolment)
                .Select(_ => UserRecoveryCode.NewCode())
                .ToList();

            user.CompleteTotpEnrolment(codes);
            _userRepository.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // The only moment these exist in readable form. Nothing stores them but the user.
            return Result<TotpEnrolmentDto>.Success(new TotpEnrolmentDto(null, null, null, codes));
        }
        catch (Exception)
        {
            // Anonymous endpoint: never echo internal detail.
            return Result<TotpEnrolmentDto>.Failure(
                "Une erreur inattendue est survenue lors de l'enrôlement. Veuillez réessayer.");
        }
    }
}
