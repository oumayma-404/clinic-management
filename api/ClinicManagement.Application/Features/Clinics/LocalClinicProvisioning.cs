using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.ProcedureTypes;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

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
    string? WorkingHoursJson = null);

/// <summary>The committed clinic and the admin account that can log into it.</summary>
public sealed record ProvisionedClinic(Clinic Clinic, User Admin);

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

        foreach (var procedureType in ProcedureTypeCatalogSeed.CreateFor(clinic.Id))
        {
            await procedureTypeRepository.AddAsync(procedureType, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Best-effort (#5): a failure here must not undo the already-committed clinic — the startup backfill
        // (IClinicCatalogSeeder.SeedAllClinicsAsync) re-seeds any clinic missing a catalog on the next boot.
        try
        {
            await clinicCatalogSeeder.SeedForClinicAsync(clinic.Id, cancellationToken);
        }
        catch
        {
            // Swallowed: the startup backfill is the safety net (see SeedAllClinicsAsync).
        }

        return Result<ProvisionedClinic>.Success(new ProvisionedClinic(clinic, admin));
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

        // A practitioner is optional, but a nameless one is never persisted — mirrors the Cloud CreateClinic and
        // JoinClinic doctor paths.
        if (request.DoctorInfo != null && !string.IsNullOrWhiteSpace(request.DoctorInfo.Specialty)
            && (string.IsNullOrWhiteSpace(request.DoctorInfo.FirstName) || string.IsNullOrWhiteSpace(request.DoctorInfo.LastName)))
        {
            return "Le prénom et le nom du praticien sont requis.";
        }

        return null;
    }
}
