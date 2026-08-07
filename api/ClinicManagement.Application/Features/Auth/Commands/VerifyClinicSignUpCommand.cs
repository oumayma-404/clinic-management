using System.Text.Json;
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
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<VerifyClinicSignUpCommandHandler> _logger;

    public VerifyClinicSignUpCommandHandler(
        IClinicSignupRepository signupRepository,
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IDoctorRepository doctorRepository,
        IProcedureTypeRepository procedureTypeRepository,
        IClinicCatalogSeeder clinicCatalogSeeder,
        IUnitOfWork unitOfWork,
        ILogger<VerifyClinicSignUpCommandHandler> logger)
    {
        _signupRepository = signupRepository;
        _clinicRepository = clinicRepository;
        _userRepository = userRepository;
        _doctorRepository = doctorRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _clinicCatalogSeeder = clinicCatalogSeeder;
        _unitOfWork = unitOfWork;
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
