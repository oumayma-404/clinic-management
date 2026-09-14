using System.Text.Json;
using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Clinics;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// The visitor opened the link in their email. Spends the token and provisions the clinic + its first admin
/// through <see cref="LocalClinicProvisioning"/> — the same construction first-run <c>setup</c> and the
/// <c>provision-clinic</c> verb use, making this its third caller and requiring no change to it.
///
/// <para><b>It issues no session</b> (AC-12): no access token, no cookie, no refresh credential. Whoever clicked
/// the link is whoever received the email, which is not the same as knowing the password — and the password is
/// the credential the visitor already chose. They sign in at <c>/login</c> like anybody else.</para>
///
/// <para>Under <c>Features/Auth/Commands</c> for the reason its sibling documents: the namespace is what
/// <c>RealtimeBroadcastBehavior</c> keys on, and this must not announce a <c>clinics</c> change to a clinic
/// group that did not exist a moment ago.</para>
/// </summary>
public class VerifyClinicSignUpCommand : IRequest<Result<ClinicSignUpVerificationDto>>
{
    public string Token { get; set; } = string.Empty;
}

/// <summary>What the verification page renders. Carries no credential and no clinic id — see AC-12.</summary>
public class ClinicSignUpVerificationDto
{
    public string Message { get; set; } = string.Empty;

    /// <summary>The clinic's name, so the page can confirm what was created rather than say « c'est fait ».</summary>
    public string ClinicName { get; set; } = string.Empty;
}

public class VerifyClinicSignUpCommandHandler
    : IRequestHandler<VerifyClinicSignUpCommand, Result<ClinicSignUpVerificationDto>>
{
    /// <summary>
    /// The single refusal shared by expired, unknown, malformed and now-taken (AC-10). One sentence for four
    /// causes because distinguishing them tells an unauthenticated caller which tokens exist and which addresses
    /// are accounts — and because a visitor's next action is the same in all four.
    /// </summary>
    private const string SharedRefusal =
        "Ce lien de vérification n'est plus valable. Il a peut-être expiré ou déjà été utilisé. "
        + "Recommencez l'inscription pour en recevoir un nouveau.";

    private readonly IClinicSignupRepository _signupRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly IClinicCatalogSeeder _clinicCatalogSeeder;
    private readonly IClinicSubscriptionRepository _subscriptionRepository;
    private readonly ISubscriptionPolicy _subscriptionPolicy;
    private readonly IMessagingAllowanceRepository _messagingAllowanceRepository;
    private readonly IMessagingAllowancePolicy _messagingAllowancePolicy;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionalEmailSender _emailSender;
    private readonly IPublicAppUrlProvider _appUrl;
    private readonly ILogger<VerifyClinicSignUpCommandHandler> _logger;

    public VerifyClinicSignUpCommandHandler(
        IClinicSignupRepository signupRepository,
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IDoctorRepository doctorRepository,
        IProcedureTypeRepository procedureTypeRepository,
        IClinicCatalogSeeder clinicCatalogSeeder,
        IClinicSubscriptionRepository subscriptionRepository,
        ISubscriptionPolicy subscriptionPolicy,
        IMessagingAllowanceRepository messagingAllowanceRepository,
        IMessagingAllowancePolicy messagingAllowancePolicy,
        IUnitOfWork unitOfWork,
        ITransactionalEmailSender emailSender,
        IPublicAppUrlProvider appUrl,
        ILogger<VerifyClinicSignUpCommandHandler> logger)
    {
        _signupRepository = signupRepository;
        _clinicRepository = clinicRepository;
        _userRepository = userRepository;
        _doctorRepository = doctorRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _clinicCatalogSeeder = clinicCatalogSeeder;
        _subscriptionRepository = subscriptionRepository;
        _subscriptionPolicy = subscriptionPolicy;
        _messagingAllowanceRepository = messagingAllowanceRepository;
        _messagingAllowancePolicy = messagingAllowancePolicy;
        _unitOfWork = unitOfWork;
        _emailSender = emailSender;
        _appUrl = appUrl;
        _logger = logger;
    }

    public async Task<Result<ClinicSignUpVerificationDto>> Handle(
        VerifyClinicSignUpCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return Refused();
            }

            var candidateHash = ClinicSignup.HashToken(request.Token.Trim());
            var signup = await _signupRepository.GetByTokenHashAsync(candidateHash, cancellationToken);

            // The lookup narrows; this is the comparison the decision rests on (AC-11), so a near-miss and a
            // wild guess take the same time to refuse.
            if (signup == null || !ClinicSignup.TokenHashMatches(signup.TokenHash, candidateHash))
            {
                return Refused();
            }

            var nowUtc = DateTime.UtcNow;
            if (!signup.IsUsable(nowUtc))
            {
                return Refused();
            }

            // The address became an account between signup and this click — somebody was provisioned for it in
            // the meantime. The link can never do anything again, so it is spent here too (AC-10): leaving it
            // live would let this branch be retried until the account disappears.
            var existingUser = await _userRepository.GetByEmailAsync(signup.Email, cancellationToken);
            if (existingUser != null)
            {
                signup.Consume(nowUtc);
                await _signupRepository.UpdateAsync(signup, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Refused();
            }

            // Staged before the provision so one SaveChangesAsync commits the clinic and the spent token
            // together — a clinic created against a token still marked usable is a second clinic waiting to be
            // created by the same link (AC-9).
            signup.Consume(nowUtc);
            await _signupRepository.UpdateAsync(signup, cancellationToken);

            var provisionRequest = new LocalClinicRequest(
                ClinicId: Guid.NewGuid(),
                Name: signup.ClinicName,
                AdminEmail: signup.Email,
                PasswordHash: signup.PasswordHash,
                FullName: signup.FullName,
                // False, and that is the whole difference from `provision-clinic`: the visitor chose this
                // password themselves at signup, so there is nothing to force them to replace (AC-13).
                MustChangePassword: false,
                Address: signup.Address,
                Phone: signup.Phone,
                City: signup.City,
                DoctorInfo: ReadDoctorInfo(signup.DoctorInfoJson),
                // Passed through raw: `ProvisionAsync` runs it through WorkingHoursSerializer.Normalize, the same
                // one first-run setup's hours go through, so both doors produce an identically-shaped Clinic.
                WorkingHoursJson: signup.WorkingHoursJson);

            // ⚠️ No ITenantScope.UseClinic here, deliberately. `provision-clinic` declares one because it has no
            // HTTP context at all; this request does, and the seed does not need a scope — ClinicCatalogSeeder
            // calls IgnoreQueryFilters() on every read, and everything else in the provision is an Add or a read
            // of the two unfiltered tables (User, Clinic).
            var provisioned = await LocalClinicProvisioning.ProvisionAsync(
                provisionRequest,
                _clinicRepository,
                _userRepository,
                _doctorRepository,
                _procedureTypeRepository,
                // The door that will create most trials — a hosted cabinet signing itself up gets its 30 free
                // days from the same helper first-run `setup` and `provision-clinic` use.
                _subscriptionRepository,
                _subscriptionPolicy,
                _messagingAllowanceRepository,
                _messagingAllowancePolicy,
                _unitOfWork,
                _clinicCatalogSeeder,
                _logger,
                cancellationToken);

            if (provisioned.IsFailure || provisioned.Value == null)
            {
                // ⚠️ The provisioning's own message must not reach an anonymous caller — it includes « Un compte
                // existe déjà avec cet email. ». Left unconsumed: a failure here may be transient (AC-10).
                _logger.LogWarning(
                    "Clinic self-signup provisioning refused for signup {SignupId}: {Reason}",
                    signup.Id, provisioned.Error);

                return Refused();
            }

            var clinic = provisioned.Value.Clinic;

            if (!provisioned.Value.CatalogsSeeded)
            {
                // The clinic is committed either way — the seed is a post-commit best effort — but say so, since
                // the startup backfill that repairs it may not run for a long time on a hosted backend.
                _logger.LogWarning(
                    "Clinic {ClinicId} was created by self-signup with unseeded reference catalogs.", clinic.Id);
            }

            await SendWelcomeAsync(signup.FullName, signup.Email, cancellationToken);

            return Result<ClinicSignUpVerificationDto>.Success(new ClinicSignUpVerificationDto
            {
                ClinicName = clinic.Name,
                Message = "Votre cabinet est créé. Vous pouvez maintenant vous connecter avec l'adresse e-mail "
                          + "et le mot de passe que vous avez choisis."
            });
        }
        catch (ConflictException ex)
        {
            // The one place this handler swallows a ConflictException: two clicks on one link race on the row's
            // xmin token, and « déjà utilisé » — AC-10's shared refusal — is the true statement, not a 409.
            _logger.LogInformation(ex, "Concurrent verification of one clinic signup token.");
            return Refused();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Clinic signup verification failed.");
            return Result<ClinicSignUpVerificationDto>.Failure(
                "La vérification n'a pas pu aboutir. Veuillez réessayer.");
        }
    }

    private static Result<ClinicSignUpVerificationDto> Refused() =>
        Result<ClinicSignUpVerificationDto>.Failure(SharedRefusal);

    /// <summary>
    /// Where a practice installs the apps, and the <b>only compiled-in host in this file</b>.
    ///
    /// <para>⚠️ <b>Not <see cref="IPublicAppUrlProvider"/>, and not the download itself.</b> That
    /// provider answers « where does <i>this deployment</i> answer? », which is the right question for a link
    /// that signs somebody in and the wrong one here: this is the vendor's own install page, the same for every
    /// hosted cabinet, and this command runs on the hosted signup door alone. The direct
    /// <c>/api/meta/client-download</c> route is deliberately <b>not</b> used —
    /// <c>site/EMAIL-TELECHARGEMENT.md</c> states why: the page warns about Windows SmartScreen's blue
    /// « Windows a protégé votre PC » screen <i>in advance</i>, with the two buttons numbered, and a
    /// dentist meeting that screen alone closes the installer and telephones.</para>
    /// </summary>
    private const string InstallPageUrl = "https://apexa.tn/#telecharger";

    /// <summary>
    /// The « bienvenue » e-mail: the cabinet exists, here is how to get into it the first time.
    ///
    /// <para><b>Why a second message rather than more of the verification one.</b> The verification e-mail is
    /// read <i>before</i> the account exists and has exactly one job — one link, one click; a walk-through
    /// of a second factor nobody can enrol yet competes with the only button that matters (see
    /// <c>EmailContent.Action</c>). This one arrives at the moment every step in it is actually possible.</para>
    ///
    /// <para>⚠️ <b>The six steps are what <c>TotpEnrolmentStep</c> and the login screen really
    /// render</b>, in that order — not a description of what enrolling a second factor is usually like. The
    /// two that exist because people trip on them: installing the authenticator is <b>its own step</b> (the app
    /// says « ouvrez votre application d'authentification » and a first-time owner has none), and the recovery
    /// codes say <i>where</i> to put them, because « conservez-les » is advice everybody follows by
    /// screenshotting the phone that is the single point of failure.</para>
    ///
    /// <para>⚠️ <b>It deliberately carries NO button.</b> Every other e-mail's call to action is a link
    /// the reader must follow; this one's would be « Me connecter », and the practices this is written for are
    /// frequently sitting in the <b>Windows shell</b> when they read it — a browser button there takes them
    /// out of the application they already have open and signs them in somewhere else, leaving two APEXAs on one
    /// desk. The way back in is « revenez dans APEXA », and the web address is a <i>row of the panel</i> for
    /// whoever is not on the shell, which is information rather than an instruction. This is the second
    /// no-button e-mail and the reason differs from <c>CompletePasswordResetCommand</c>'s: that one asks for
    /// nothing, this one asks for something the reader reaches without leaving where they are.</para>
    ///
    /// <para>⚠️ <b>Best-effort, and it must never fail the verification.</b> The cabinet is committed
    /// by the time this runs; a mail server having a bad minute cannot be allowed to answer the visitor with
    /// « la vérification n'a pas pu aboutir » over a clinic that exists — they would start again and
    /// meet AC-10's « ce lien n'est plus valable ». The screen's own message already tells them they can sign
    /// in, so this e-mail is a convenience, not the channel.</para>
    /// </summary>
    private async Task SendWelcomeAsync(string fullName, string email, CancellationToken cancellationToken)
    {
        try
        {
            var sent = await _emailSender.SendAsync(
                email,
                "Votre cabinet est créé — vos premiers pas sur APEXA",
                new EmailContent
                {
                    Title = "Bienvenue sur APEXA",
                    Preheader =
                        "Votre cabinet est créé. Six étapes pour votre première connexion.",
                    Greeting = EmailGreeting.For(fullName),
                    Intro =
                    [
                        "Votre cabinet est créé et votre compte administrateur est prêt. Votre "
                        + "première connexion demande une étape de plus que les suivantes : APEXA "
                        + "protège les dossiers de vos patients avec un code à six chiffres, en plus de "
                        + "votre mot de passe. Vous ne le mettez en place qu'une seule fois."
                    ],
                    Details =
                    [
                        new EmailDetail("Votre identifiant", email),
                        new EmailDetail("APEXA dans un navigateur", $"{_appUrl.BaseUrl}/login", IsLink: true)
                    ],
                    StepsTitle = "Votre première connexion, étape par étape",
                    Steps =
                    [
                        new EmailStep(
                            "Installez Google Authenticator sur votre téléphone",
                            "Gratuit, sur le Play Store ou l'App Store. Microsoft Authenticator et FreeOTP "
                            + "fonctionnent aussi si vous en avez déjà un."),
                        new EmailStep(
                            "Revenez dans APEXA et saisissez votre identifiant et votre mot de passe",
                            "Dans l'application Windows si vous l'avez installée — sinon à l'adresse "
                            + "ci-dessus, dans votre navigateur. Le mot de passe est celui que vous venez de "
                            + "choisir à l'inscription."),
                        new EmailStep(
                            "APEXA affiche un QR code : scannez-le avec l'application",
                            "Dans Google Authenticator, touchez « + » puis « Scanner un QR code ». Si vous lisez "
                            + "ce message sur le téléphone lui-même, utilisez plutôt le bouton "
                            + "« Ouvrir dans mon application d'authentification »."),
                        new EmailStep(
                            "Recopiez dans APEXA le code à six chiffres affiché par l'application",
                            "Il change toutes les trente secondes, c'est normal."),
                        new EmailStep(
                            "Mettez de côté les huit codes de secours qu'APEXA vous donne",
                            "Imprimez-les ou notez-les, et rangez-les ailleurs que dans le téléphone : "
                            + "ils servent précisément le jour où vous ne l'avez plus. Ils ne sont "
                            + "affichés qu'une fois."),
                        new EmailStep(
                            "Connectez-vous avec un nouveau code à six chiffres",
                            "Vous êtes dans votre cabinet. À chaque connexion suivante : mot de passe, "
                            + "puis le code du moment.")
                    ],
                    Outro =
                    [
                        "Le même compte ouvre APEXA partout : l'application Windows sur le poste du "
                        + "cabinet, l'application Android sur un téléphone, ou simplement un navigateur — "
                        + "vous n'avez rien à recréer d'un appareil à l'autre.",
                        "Les applications Windows et Android se téléchargent sur " + InstallPageUrl
                        + ". La page prévient de l'écran bleu « Windows a protégé votre PC » "
                        + "que Windows affiche à la première installation — ce n'est pas une alerte de "
                        + "virus, et elle montre où cliquer. Sur iPhone, il n'y a rien à installer : ouvrez "
                        + "l'adresse dans Safari, touchez « Partager » puis « Sur l'écran "
                        + "d'accueil »."
                    ],
                    Note =
                        "Une question, un doute, quelque chose qui ne se passe pas comme décrit ? "
                        + "Répondez simplement à ce message."
                },
                cancellationToken);

            if (sent.Outcome != TransactionalEmailOutcome.Sent)
            {
                _logger.LogWarning(
                    "Welcome email could not be sent after clinic self-signup verification: {Outcome} {Reason}",
                    sent.Outcome, sent.Error);
            }
        }
        catch (Exception ex)
        {
            // Swallowed on purpose: see the remarks above. The clinic is already committed.
            _logger.LogError(ex, "Welcome email failed after clinic self-signup verification.");
        }
    }

    /// <summary>
    /// A stored practitioner block that no longer deserializes is treated as absent rather than fatal: the
    /// clinic and its admin are what the visitor is waiting for, and « Mon profil » can be filled in afterwards.
    /// </summary>
    private DoctorPersonalInfoDto? ReadDoctorInfo(string? doctorInfoJson)
    {
        if (string.IsNullOrWhiteSpace(doctorInfoJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DoctorPersonalInfoDto>(doctorInfoJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "A clinic signup carried an unreadable practitioner block; ignoring it.");
            return null;
        }
    }
}
