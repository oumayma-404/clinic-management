using System.Security.Cryptography;
using System.Text.Json;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Clinics;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// A visitor asks for a clinic on this hosted backend. Writes a pending <see cref="ClinicSignup"/> and emails a
/// verification link; creates <b>nothing</b> real (AC-2).
///
/// <para><b>Why it lives under <c>Features/Auth/Commands</c> and not <c>Features/Clinics</c>.</b>
/// <c>RealtimeBroadcastBehavior</c> derives its resource key from the namespace, so the same handler under
/// <c>Clinics</c> would broadcast <c>clinics</c> to a SignalR group — announcing a clinic that does not exist to
/// clients of a clinic this request has nothing to do with. <c>Auth</c> is on the behavior's excluded list.</para>
/// </summary>
public class SignUpClinicCommand : IRequest<Result<ClinicSignUpResultDto>>
{
    public string ClinicName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }

    /// <summary>Optional: the signer-up is also the cabinet's practitioner (the single-dentist case).</summary>
    public DoctorPersonalInfoDto? DoctorInfo { get; set; }

    /// <summary>
    /// The wizard's « Horaires » step, in <c>Clinic.WorkingHoursJson</c>'s own shape. Carried opaquely to the
    /// pending row and normalised only at provisioning, by the same <c>WorkingHoursSerializer</c> first-run setup
    /// uses — parsing it here would be a second reading of the format and the one that drifts.
    /// </summary>
    public string? WorkingHoursJson { get; set; }
}

/// <summary>
/// What the visitor is told. Deliberately one neutral sentence and nothing else — no id, no expiry, no echo of
/// the address — so the response is byte-identical whether the email was free, already an account, or already
/// had a pending signup (AC-3).
/// </summary>
public class ClinicSignUpResultDto
{
    public string Message { get; set; } = string.Empty;
}

public class SignUpClinicCommandHandler
    : IRequestHandler<SignUpClinicCommand, Result<ClinicSignUpResultDto>>
{
    /// <summary>
    /// The one sentence every outcome of a well-formed submission gets. It says « if the address is eligible »
    /// rather than « we sent it » on purpose: it has to be true when nothing was sent because the address is
    /// already an account, and a sentence that is a lie in one branch is a sentence somebody will later "fix"
    /// into an enumeration oracle.
    /// </summary>
    private const string NeutralAcknowledgement =
        "Si cette adresse peut être utilisée, un lien de vérification vient de lui être envoyé. "
        + "Le lien est valable 24 heures.";

    /// <summary>
    /// Refusal for a deployment that cannot complete a signup at all. It names nothing internal: an unauthenticated
    /// caller learning this backend's configuration schema, and that its mail transport is down, is a fingerprint
    /// and an abuse window. The operator's version of the same fact goes to the log.
    /// </summary>
    private const string ServiceUnavailable =
        "L'inscription en ligne est momentanément indisponible. Réessayez plus tard.";

    /// <summary>Lets the controller answer 503 rather than 400 — nothing about the request was malformed.</summary>
    public const string UnavailableCode = "signup_unavailable";

    /// <summary>How long a spent row is kept before the opportunistic purge drops it (AC-7).</summary>
    private static readonly TimeSpan ConsumedRetention = TimeSpan.FromDays(30);

    /// <summary>
    /// The minimum gap between two verification emails to one address. Without it a caller can re-send on every
    /// request the limiter allows — and the limiter partitions on the <i>submitted</i> address, which the caller
    /// chooses, so it is no bound at all on mail aimed at one victim's mailbox.
    /// </summary>
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(2);

    private readonly IClinicSignupRepository _signupRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILocalAuthService _localAuthService;
    private readonly ITransactionalEmailSender _emailSender;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublicAppUrlProvider _appUrl;
    private readonly ILogger<SignUpClinicCommandHandler> _logger;

    public SignUpClinicCommandHandler(
        IClinicSignupRepository signupRepository,
        IUserRepository userRepository,
        ILocalAuthService localAuthService,
        ITransactionalEmailSender emailSender,
        IUnitOfWork unitOfWork,
        IPublicAppUrlProvider appUrl,
        ILogger<SignUpClinicCommandHandler> logger)
    {
        _signupRepository = signupRepository;
        _userRepository = userRepository;
        _localAuthService = localAuthService;
        _emailSender = emailSender;
        _unitOfWork = unitOfWork;
        _appUrl = appUrl;
        _logger = logger;
    }

    public async Task<Result<ClinicSignUpResultDto>> Handle(
        SignUpClinicCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var refusal = Validate(request, out var canonicalEmail);
            if (refusal != null)
            {
                return Result<ClinicSignUpResultDto>.Failure(refusal);
            }

            // Before anything is written (AC-15), and both are equally fatal and silent afterwards: no mail host
            // sends nothing, and no FrontendUrl points every link at the recipient's own machine.
            if (!_emailSender.IsConfigured || !_appUrl.IsConfigured)
            {
                _logger.LogError(
                    "Clinic self-signup is unreachable: mail configured={MailConfigured}, "
                    + "FrontendUrl configured={UrlConfigured}.",
                    _emailSender.IsConfigured, _appUrl.IsConfigured);

                return Result<ClinicSignUpResultDto>.Failure(ServiceUnavailable, UnavailableCode);
            }

            var email = ClinicSignup.NormalizeEmail(canonicalEmail);
            var nowUtc = DateTime.UtcNow;

            // AC-7: the table only grows when somebody signs up, so this is the write that owes the trim. It
            // commits on its own and is bounded — see the repository for the 409 that staging it here produced.
            var purged = await _signupRepository.PurgeSpentAsync(nowUtc, ConsumedRetention, cancellationToken);
            if (purged > 0)
            {
                _logger.LogInformation("Purged {Count} spent clinic signup(s).", purged);
            }

            // Hashed BEFORE the account check (AC-3): PBKDF2's tens of milliseconds spent on only the free-address
            // branch made the two measurably different lengths — an enumeration oracle by stopwatch.
            var passwordHash = _localAuthService.HashPassword(request.Password);

            // The address already has an account — so this visitor either already signed up and verified, or is
            // probing. Write nothing, send nothing, answer exactly as the happy path does (AC-3).
            var existingUser = await _userRepository.GetByEmailAsync(email, cancellationToken);
            if (existingUser != null)
            {
                return Acknowledged();
            }

            var doctorInfoJson = request.DoctorInfo == null ? null : JsonSerializer.Serialize(request.DoctorInfo);
            var signup = await _signupRepository.GetByEmailAsync(email, cancellationToken);

            if (signup != null && signup.IsUsable(nowUtc))
            {
                return await ResendAsync(signup, nowUtc, cancellationToken);
            }

            return await IssueAsync(
                signup, request, email, passwordHash, doctorInfoJson, nowUtc, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two signups for one address in the same tick: the loser losing is correct, but a distinguishable
            // failure would re-open the AC-3 oracle — and the winner's email really was sent.
            _logger.LogInformation(ex, "Concurrent clinic signup for one address; the first submission won.");
            return Acknowledged();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Clinic signup failed.");
            return Result<ClinicSignUpResultDto>.Failure(
                "L'inscription n'a pas pu aboutir. Veuillez réessayer.");
        }
    }

    /// <summary>
    /// A second submission for an address whose token is <b>still live</b>.
    ///
    /// <para>⚠️ <b>It does not re-read the submitted details, and that is the whole point.</b> Overwriting them
    /// let an anonymous caller replace a stranger's pending <c>PasswordHash</c> and clinic name: the victim's own
    /// link died, the replacement arrived in the victim's own inbox looking identical, and clicking it provisioned
    /// their clinic against the sender's password. The first submission for an address owns its credentials.</para>
    ///
    /// <para>⚠️ <b>The row is rotated only after the send succeeded</b>, the reverse of the issue path's ordering:
    /// here a failure has something to fall back on — the link already in the visitor's inbox — and rotating first
    /// would kill it to replace it with one that never arrived.</para>
    /// </summary>
    private async Task<Result<ClinicSignUpResultDto>> ResendAsync(
        ClinicSignup signup, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (nowUtc - signup.LastIssuedAtUtc() < ResendCooldown)
        {
            return Acknowledged();
        }

        var rawToken = GenerateToken();
        if (!await SendVerificationAsync(signup, signup.Email, rawToken, cancellationToken))
        {
            return Acknowledged();
        }

        signup.Reissue(ClinicSignup.HashToken(rawToken), nowUtc);
        signup.RecordEmailSendAttempt();
        await _signupRepository.UpdateAsync(signup, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Acknowledged();
    }

    /// <summary>
    /// A first submission, or one replacing a row that can no longer verify anything (expired or consumed). Here
    /// the details <i>are</i> taken from this request — there is no live link and no earlier submission to protect
    /// — and the row is committed before the send, so a mail failure leaves a row the visitor's retry re-arms
    /// rather than a live link with nothing behind it.
    /// </summary>
    private async Task<Result<ClinicSignUpResultDto>> IssueAsync(
        ClinicSignup? existing,
        SignUpClinicCommand request,
        string email,
        string passwordHash,
        string? doctorInfoJson,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var rawToken = GenerateToken();
        var tokenHash = ClinicSignup.HashToken(rawToken);

        ClinicSignup signup;
        if (existing == null)
        {
            signup = ClinicSignup.Create(
                request.ClinicName, request.FullName, email, passwordHash, tokenHash, nowUtc,
                request.Phone, request.Address, request.City, doctorInfoJson, request.WorkingHoursJson);
            signup.RecordEmailSendAttempt();
            await _signupRepository.AddAsync(signup, cancellationToken);
        }
        else
        {
            signup = existing;
            signup.Renew(
                request.ClinicName, request.FullName, passwordHash, tokenHash, nowUtc,
                request.Phone, request.Address, request.City, doctorInfoJson, request.WorkingHoursJson);
            signup.RecordEmailSendAttempt();
            await _signupRepository.UpdateAsync(signup, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await SendVerificationAsync(signup, email, rawToken, cancellationToken);

        return Acknowledged();
    }

    /// <summary>
    /// ⚠️ A failed send is reported to the <b>log</b> and never to the visitor. The refusal it used to return was
    /// a clean enumeration oracle: during any mail outage a free address got « l'e-mail n'a pas pu être envoyé »
    /// while an address that is already an account got the neutral sentence, with no timing needed to tell them
    /// apart. AC-15's loud refusal is for a mail host that is <i>unconfigured</i> — checked before anything is
    /// written — not for one that is having a bad minute.
    /// </summary>
    private async Task<bool> SendVerificationAsync(
        ClinicSignup signup, string email, string rawToken, CancellationToken cancellationToken)
    {
        var sent = await _emailSender.SendAsync(
            email, EmailSubject, BuildEmailBody(signup.FullName, rawToken), cancellationToken);

        if (sent.Outcome == TransactionalEmailOutcome.Sent)
        {
            return true;
        }

        _logger.LogWarning(
            "Clinic signup verification email could not be sent for signup {SignupId} "
            + "(attempt {Attempts}): {Outcome} {Reason}",
            signup.Id, signup.EmailSendAttempts, sent.Outcome, sent.Error);

        return false;
    }

    private static Result<ClinicSignUpResultDto> Acknowledged() =>
        Result<ClinicSignUpResultDto>.Success(new ClinicSignUpResultDto { Message = NeutralAcknowledgement });

    /// <summary>
    /// The refusals that reveal nothing about the address: a missing field, a malformed one, an over-long one, and
    /// the password length (AC-4 — a length rule is a fact about what was typed, not about who owns the mailbox).
    /// </summary>
    private static string? Validate(SignUpClinicCommand request, out string canonicalEmail)
    {
        canonicalEmail = string.Empty;

        if (string.IsNullOrWhiteSpace(request.ClinicName)) return "Le nom du cabinet est requis.";
        if (string.IsNullOrWhiteSpace(request.FullName)) return "Le nom complet est requis.";
        if (string.IsNullOrWhiteSpace(request.Email)) return "L'email est requis.";

        var emailRefusal = ReadEmailAddress(request.Email, out canonicalEmail);
        if (emailRefusal != null) return emailRefusal;

        if (string.IsNullOrWhiteSpace(request.Password)) return "Le mot de passe est requis.";

        if (request.Password.Length < PasswordPolicy.MinLength)
        {
            return $"Le mot de passe doit contenir au moins {PasswordPolicy.MinLength} caractères.";
        }

        // Bounded here rather than by the column: a value the insert refuses surfaces as the generic « réessayer »,
        // naming no field and inviting a retry nothing can fix. DoctorInfoJson is `text` and has no bound at all.
        var tooLong = TooLong(request.ClinicName, ClinicSignup.MaxClinicNameLength, "Le nom du cabinet")
                      ?? TooLong(request.FullName, ClinicSignup.MaxFullNameLength, "Le nom complet")
                      ?? TooLong(canonicalEmail, ClinicSignup.MaxEmailLength, "L'adresse e-mail")
                      ?? TooLong(request.Phone, ClinicSignup.MaxPhoneLength, "Le téléphone")
                      ?? TooLong(request.Address, ClinicSignup.MaxAddressLength, "L'adresse")
                      ?? TooLong(request.City, ClinicSignup.MaxCityLength, "Le gouvernorat");
        if (tooLong != null) return tooLong;

        if (request.DoctorInfo != null)
        {
            var practitionerTooLong =
                TooLong(request.DoctorInfo.FirstName, ClinicSignup.MaxFullNameLength, "Le prénom du praticien")
                ?? TooLong(request.DoctorInfo.LastName, ClinicSignup.MaxFullNameLength, "Le nom du praticien")
                ?? TooLong(request.DoctorInfo.Specialty, ClinicSignup.MaxCityLength, "La spécialité")
                ?? TooLong(request.DoctorInfo.Phone, ClinicSignup.MaxPhoneLength, "Le téléphone du praticien");
            if (practitionerTooLong != null) return practitionerTooLong;
        }

        // One body for « what is a usable practitioner block? », shared with the provisioning that will act on it
        // hours later — validating there instead is useless, since the visitor cannot correct anything from a link.
        return LocalClinicProvisioning.ValidatePractitioner(request.DoctorInfo);
    }

    private static string? TooLong(string? value, int maxLength, string fieldLabel) =>
        value != null && value.Trim().Length > maxLength
            ? $"{fieldLabel} ne peut pas dépasser {maxLength} caractères."
            : null;

    /// <summary>
    /// Validates the address <b>and returns the canonical form</b>, which is what must be stored.
    ///
    /// <para>⚠️ Parsing and keeping the raw string are not the same thing, and the gap was exploitable:
    /// <c>MailAddress</c> accepts the display-name form, so <c>Attaquant &lt;dr@cabinet.tn&gt;</c> parsed happily
    /// and was stored verbatim — matching no <c>User</c> row (so both the already-an-account and the now-taken
    /// guards missed), unique per variant (so « one row per address » collapsed and unlimited mails could be aimed
    /// at one mailbox), and, if verified, producing an account whose email no login form can reproduce. Requiring
    /// the parsed address to round-trip the input is what closes all three.</para>
    /// </summary>
    private static string? ReadEmailAddress(string value, out string canonical)
    {
        canonical = string.Empty;

        var trimmed = value.Trim();
        if (!System.Net.Mail.MailAddress.TryCreate(trimmed, out var parsed) || parsed == null)
        {
            return "L'adresse e-mail n'est pas valide.";
        }

        if (!string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return "Saisissez uniquement l'adresse e-mail, sans nom ni chevrons.";
        }

        canonical = parsed.Address;
        return null;
    }

    /// <summary>PostgreSQL 23505 — matched on the type name, following <c>UnitOfWork.IsExclusionViolation</c>.</summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
        {
            if (inner.GetType().FullName != "Npgsql.PostgresException")
            {
                continue;
            }

            var sqlState = inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string;
            if (sqlState == "23505")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 32 bytes from the OS CSPRNG, URL-safe. <c>Random</c> is not an option: this is the only thing between the
    /// internet and a provisioned clinic.
    /// </summary>
    private static string GenerateToken() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private const string EmailSubject = "Vérifiez votre adresse pour créer votre cabinet";

    private string BuildEmailBody(string fullName, string rawToken) =>
        $"""
        Bonjour {fullName},

        Vous venez de demander la création de votre cabinet. Pour finaliser, ouvrez le lien ci-dessous :

        {BuildVerificationLink(rawToken)}

        Ce lien est valable 24 heures et ne peut servir qu'une seule fois. Votre cabinet ne sera créé
        qu'après cette vérification.

        Si vous n'êtes pas à l'origine de cette demande, ignorez simplement ce message : aucun compte
        n'a été créé.
        """;

    /// <summary>
    /// Built from <see cref="IPublicAppUrlProvider"/>, i.e. from <c>FrontendUrl</c> — so no host is compiled in
    /// (AC-16) and one deployment's link never points at another's front door.
    ///
    /// <para>⚠️ The token rides in the <b>fragment</b>, not the query string. A fragment is never sent to the
    /// server, so the live single-use credential stays out of the reverse proxy's access log and out of every
    /// intermediate hop — all of which outlive the 24 h the token is supposed to be bounded by.</para>
    /// </summary>
    private string BuildVerificationLink(string rawToken) =>
        $"{_appUrl.BaseUrl}/signup/verifier#token={Uri.EscapeDataString(rawToken)}";
}
