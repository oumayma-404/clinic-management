using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.ProcedureTypes;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Clinics;

/// <summary>What a caller has to supply to stand up a clinic and its first password-backed admin.</summary>
/// <param name="ClinicId">
/// Minted by the caller, not here, for one reason: <c>provision-clinic</c> runs with no HTTP context and must
/// declare <c>ITenantScope.UseClinic(id)</c> — which it can only do if it knows the id <b>before</b> the work
/// starts. A scope declared afterwards would cover nothing.
/// </param>
/// <param name="MustChangePassword">
/// True when the password was <b>generated for</b> the admin rather than chosen by them — the
/// <c>provision-clinic</c> verb prints a one-time password, so the account must be forced to replace it. First-run
/// <c>setup</c> passes false: the owner typed the password themselves and there is nobody to hand it to.
/// </param>
public sealed record LocalClinicRequest(
    Guid ClinicId,
    string? Name,
    string? AdminEmail,
    string PasswordHash,
    string? FullName,
    bool MustChangePassword,
    string? Address = null,
    string? Phone = null,
    string? City = null,
    DoctorPersonalInfoDto? DoctorInfo = null,
    string? WorkingHoursJson = null)
{
    /// <summary>
    /// Replaces the compiler-generated printer so <see cref="PasswordHash"/> cannot reach a log through a
    /// destructured template or an interpolated exception message. A PBKDF2 hash is not a password, but it is the
    /// verifier for one and belongs in no log; this record is constructed on the two paths that mint an
    /// administrator, which is exactly where such a line would be written.
    /// </summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ClinicId = ").Append(ClinicId)
            .Append(", Name = ").Append(Name)
            .Append(", AdminEmail = ").Append(AdminEmail)
            .Append(", FullName = ").Append(FullName)
            .Append(", MustChangePassword = ").Append(MustChangePassword)
            .Append(", PasswordHash = ***");
        return true;
    }
}

/// <summary>The committed clinic and the admin account that can log into it.</summary>
/// <param name="CatalogsSeeded">
/// False when the reference-catalog seed failed. The clinic is committed either way — the seed is a best-effort
/// post-commit side effect — but `provision-clinic` has to be able to *say so*, because it otherwise prints
/// « Clinic provisioned successfully. » over a clinic with no CNAM, medication or dental-act catalogue, and on a
/// hosted backend the startup backfill that repairs it may not run for months.
/// </param>
public sealed record ProvisionedClinic(Clinic Clinic, User Admin, bool CatalogsSeeded = true);

/// <summary>
/// Creates a clinic together with its first local (email + password) administrator — the **single** definition of
/// that operation.
///
/// <para><b>Why it is here and not inside <c>CreateClinicCommandHandler</c>.</b> Two callers need it and they
/// cannot share a MediatR command. First-run <c>setup</c> is an HTTP request; <c>provision-clinic</c>
/// (<c>multi-tenant-cloud</c> US-3) is a console verb whose container is built from <c>AddInfrastructure</c> alone,
/// with no mediator, no <c>IClinicContext</c> and no HTTP context to read a caller from. Copying the body into the
/// verb is the repo's own <c>fixes-dont-propagate</c> shape — « what it means to create a clinic » would have two
/// answers and the seldom-used one would rot. So it was **moved**, not copied, the same way
/// <c>PatientFromRequest.Build</c> was lifted out of <c>CreatePatientCommandHandler</c>.</para>
///
/// <para>⚠️ <b>It deliberately does not decide whether the caller is allowed to do this.</b> Setup's one-time
/// bootstrap gate (<c>AnyUserExistsAsync</c>, AC-1.2a) stays in the handler: it is exactly the rule
/// <c>provision-clinic</c> must not obey, since provisioning clinic #2 of a hosted install happens precisely when
/// users already exist. Authorization is the call site's business; this is construction.</para>
/// </summary>
public static class LocalClinicProvisioning
{
    /// <summary>
    /// Validates the request, mints a unique join code, and commits the clinic, its admin, an optional linked
    /// <see cref="Doctor"/> and the default procedure menu in one save — then seeds the reference catalogs
    /// best-effort.
    /// </summary>
    public static async Task<Result<ProvisionedClinic>> ProvisionAsync(
        LocalClinicRequest request,
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IDoctorRepository doctorRepository,
        IProcedureTypeRepository procedureTypeRepository,
        IUnitOfWork unitOfWork,
        IClinicCatalogSeeder clinicCatalogSeeder,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var refusal = Validate(request);
        if (refusal != null)
        {
            return Result<ProvisionedClinic>.Failure(refusal);
        }

        // The partial unique index on the lowercased email would otherwise surface as a DbUpdateException, i.e. a
        // 500 on setup and a stack trace on the operator's console. First-run reaches this with no users at all,
        // so the check costs it nothing and covers the verb, which is the caller that can genuinely collide.
        var existing = await userRepository.GetByEmailAsync(request.AdminEmail!, cancellationToken);
        if (existing != null)
        {
            return Result<ProvisionedClinic>.Failure("Un compte existe déjà avec cet email.");
        }

        var code = ClinicCodeGenerator.Generate();
        while (await clinicRepository.CodeExistsAsync(code, cancellationToken))
        {
            code = ClinicCodeGenerator.Generate();
        }

        var clinic = new Clinic(
            request.ClinicId,
            request.Name!,
            request.Address,
            request.Phone,
            request.AdminEmail,
            code,
            request.City);

        // Persist the onboarding wizard's working hours (finding #16), normalized like UpdateClinicCommand.
        var normalizedWorkingHours = WorkingHoursSerializer.Normalize(request.WorkingHoursJson);
        if (normalizedWorkingHours != null)
        {
            clinic.SetWorkingHours(normalizedWorkingHours);
        }

        await clinicRepository.AddAsync(clinic, cancellationToken);

        var admin = User.CreateLocalUser(
            clinic.Id,
            User.RoleAdmin,
            request.AdminEmail!,
            request.PasswordHash,
            request.FullName!,
            request.MustChangePassword);
        await userRepository.AddAsync(admin, cancellationToken);

        // Single-dentist cabinet: when the first admin is also the practitioner, create + link a Doctor so their
        // document identity (cachet, CNOMDT ordre) and « Mon profil » work. The admin keeps the "admin" role.
        // Absent DoctorInfo → an admin-only account (e.g. a non-clinical office manager).
        if (request.DoctorInfo != null && !string.IsNullOrWhiteSpace(request.DoctorInfo.Specialty))
        {
            var doctor = new Doctor(
                Guid.NewGuid(),
                clinic.Id,
                request.DoctorInfo.FirstName,
                request.DoctorInfo.LastName,
                request.DoctorInfo.Specialty,
                request.DoctorInfo.Phone,
                request.AdminEmail);
            doctor.LinkToUser(admin.Id);
            await doctorRepository.AddAsync(doctor, cancellationToken);
        }

        await SeedDefaultProcedureTypesAsync(clinic.Id, procedureTypeRepository, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var catalogsSeeded = await TrySeedCatalogsAsync(
            clinic.Id, clinicCatalogSeeder, logger, cancellationToken);

        return Result<ProvisionedClinic>.Success(new ProvisionedClinic(clinic, admin, catalogsSeeded));
    }

    /// <summary>
    /// Stages the clinic's starting procedure menu (the common Tunisian dental procedures, all editable).
    ///
    /// <para>⚠️ Shared with <c>CreateClinicCommandHandler</c>'s <b>Cloud</b> branch (review finding 33). This class
    /// claims to be « the single definition » of creating a clinic and cites <c>fixes-dont-propagate</c> by name, but
    /// the extraction covered the Local branch only — leaving a byte-identical private copy of this loop and of the
    /// catalog seed below in the handler, i.e. two answers to « what a new clinic starts with », the seldom-changed
    /// one being the copy the helper was written to eliminate.</para>
    /// </summary>
    public static async Task SeedDefaultProcedureTypesAsync(
        Guid clinicId,
        IProcedureTypeRepository procedureTypeRepository,
        CancellationToken cancellationToken = default)
    {
        foreach (var procedureType in ProcedureTypeCatalogSeed.CreateFor(clinicId))
        {
            await procedureTypeRepository.AddAsync(procedureType, cancellationToken);
        }
    }

    /// <summary>
    /// Seeds the clinic's reference catalogs (CNAM / medications / dental acts) best-effort, returning whether it
    /// worked. Post-commit by design: a failure must not undo the already-created clinic, since the startup backfill
    /// (<c>IClinicCatalogSeeder.SeedAllClinicsAsync</c>) re-seeds any clinic missing one on the next boot.
    ///
    /// <para>⚠️ It <b>logs</b> rather than swallowing silently (review finding 20), and returns the outcome so a
    /// caller can say so: <c>provision-clinic</c> otherwise prints « Clinic provisioned successfully. » and a
    /// password over a clinic with no catalogue at all, and on a hosted backend the safety net named above may not
    /// run for months.</para>
    /// </summary>
    public static async Task<bool> TrySeedCatalogsAsync(
        Guid clinicId,
        IClinicCatalogSeeder clinicCatalogSeeder,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await clinicCatalogSeeder.SeedForClinicAsync(clinicId, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Reference catalogs could not be seeded for clinic {ClinicId}; the startup backfill will retry on the "
                + "next boot.",
                clinicId);
            return false;
        }
    }

    /// <summary>
    /// The rules both callers share; the French refusal, or null when the request is sound. The password itself is
    /// <b>not</b> checked here — it arrives already hashed, and only the caller knows whether it was typed by a
    /// human (the length policy applies) or generated (it cannot fail).
    /// </summary>
    private static string? Validate(LocalClinicRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "Le nom du cabinet est requis.";
        if (string.IsNullOrWhiteSpace(request.AdminEmail)) return "L'email est requis.";
        if (string.IsNullOrWhiteSpace(request.FullName)) return "Le nom complet est requis.";
        if (string.IsNullOrWhiteSpace(request.PasswordHash)) return "Le mot de passe est requis.";
        if (request.ClinicId == Guid.Empty) return "L'identifiant du cabinet est requis.";

        return ValidatePractitioner(request.DoctorInfo);
    }

    /// <summary>
    /// A practitioner is optional, but a half-filled one is never persisted — mirrors the Cloud CreateClinic and
    /// JoinClinic doctor paths.
    ///
    /// <para><b>Public because clinic self-signup has to apply it hours earlier.</b> That path validates at signup
    /// rather than at verification (refusing on the emailed link is useless — the visitor cannot correct anything
    /// from there), so without a shared body the rule would exist twice, French wording included, and drift the
    /// first time either half changed.</para>
    /// </summary>
    public static string? ValidatePractitioner(DoctorPersonalInfoDto? doctorInfo)
    {
        if (doctorInfo == null)
        {
            return null;
        }

        var hasName = !string.IsNullOrWhiteSpace(doctorInfo.FirstName)
                      && !string.IsNullOrWhiteSpace(doctorInfo.LastName);
        var hasSpecialty = !string.IsNullOrWhiteSpace(doctorInfo.Specialty);

        if (hasSpecialty && !hasName)
        {
            return "Le prénom et le nom du praticien sont requis.";
        }

        // The reverse case, which used to pass silently and create no Doctor at all: only Specialty decides
        // whether the block is persisted, so a visitor who typed their name and skipped the select was promised a
        // fiche praticien and got none.
        if (hasName && !hasSpecialty)
        {
            return "Choisissez la spécialité du praticien, ou laissez la section praticien vide.";
        }

        return null;
    }
}
