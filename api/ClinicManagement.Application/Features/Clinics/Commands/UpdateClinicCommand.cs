using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Commands;

public class UpdateClinicCommand : IRequest<Result<ClinicDto>>
{
    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the user
    /// actually edited rather than the one the handler just loaded. Omit (0) to skip the check — the seam
    /// server-internal writers use; see <c>IUnitOfWork.SetExpectedVersion</c>.
    /// </summary>
    public uint Version { get; set; }

    public string Name { get; set; } = string.Empty;

    /*
     * ⚠️ Band A — the five nullable strings here are TRI-STATE, and each carries a companion flag saying whether
     * the request mentioned it at all. Without them « cleared » and « not sent » are the same null, and the
     * handler read null as « leave unchanged » — so a matricule fiscal, a ville or a gouvernorat, once saved,
     * could never be cleared: the blank save reported success and the old value came back on reload. The API
     * layer sets the flags from form-key presence; see `UpdateClinicRequest`.
     */
    public string? Address { get; set; }
    public bool AddressSpecified { get; set; }
    public string? City { get; set; }
    public bool CitySpecified { get; set; }
    public string? Phone { get; set; }
    public bool PhoneSpecified { get; set; }
    public string? Email { get; set; }
    public bool EmailSpecified { get; set; }
    public Stream? LogoFile { get; set; }
    public string? LogoFileName { get; set; }
    public long LogoLength { get; set; }

    // Billing / note-d'honoraires settings. Null = leave the current value unchanged, except the matricule,
    // which is tri-state for the reason recorded above.
    public string? MatriculeFiscal { get; set; }
    public bool MatriculeFiscalSpecified { get; set; }
    public bool? VatApplicable { get; set; }
    public decimal? VatRate { get; set; }
    public bool? StampDutyEnabled { get; set; }
    public decimal? StampDutyAmount { get; set; }

    // Working hours JSON array (reliability-and-polish AC-7). Null/blank = leave the current value unchanged.
    public string? WorkingHoursJson { get; set; }
}

public class UpdateClinicCommandHandler : IRequestHandler<UpdateClinicCommand, Result<ClinicDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateClinicCommandHandler> _logger;

    public UpdateClinicCommandHandler(
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ILogger<UpdateClinicCommandHandler> logger)
    {
        _clinicRepository = clinicRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ClinicDto>> Handle(UpdateClinicCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Get user ID from token
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<ClinicDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<ClinicDto>.Failure("Utilisateur introuvable.");
            }

            var clinicId = user.ClinicId;

            // Get clinic from database
            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<ClinicDto>.Failure("Clinique introuvable.");
            }

            // Audit § 2, finding 7: PUT /api/clinics carried NO role policy, so a secretary could change the
            // clinic's legal billing identity — matricule fiscal, TVA applicable/rate, timbre fiscal. Those
            // values are frozen onto every invoice issued afterwards, which makes them a different class of
            // setting from the phone number.
            //
            // Gated per FIELD rather than by closing the endpoint, on a desired-vs-current comparison. That
            // matters for a real reason: the settings form submits the
            // whole card, so a secretary correcting the clinic phone re-sends matricule fiscal and TVA at
            // their existing values. Comparing against the stored value means an unchanged field is not a
            // change, and only an actual edit is refused (spec EC-11).
            var legalBillingChanging =
                (request.MatriculeFiscalSpecified && !string.Equals(request.MatriculeFiscal, clinic.MatriculeFiscal, StringComparison.Ordinal))
                || (request.VatApplicable.HasValue && request.VatApplicable.Value != clinic.VatApplicable)
                || (request.VatRate.HasValue && request.VatRate.Value != clinic.VatRate)
                || (request.StampDutyEnabled.HasValue && request.StampDutyEnabled.Value != clinic.StampDutyEnabled)
                || (request.StampDutyAmount.HasValue && request.StampDutyAmount.Value != clinic.StampDutyAmount);

            // Refused BEFORE the logo upload below, so an unauthorized caller never writes to storage.
            if (legalBillingChanging && !user.IsAdmin())
            {
                return Result<ClinicDto>.Failure(
                    "Seul un administrateur peut modifier les paramètres de facturation " +
                    "(matricule fiscal, TVA, timbre fiscal).");
            }

            // AC-7: validate the working-hours payload up front (before any logo upload) so a bad payload
            // fails fast; a blank/omitted payload leaves the stored hours unchanged.
            string? normalizedWorkingHours = null;
            if (!string.IsNullOrWhiteSpace(request.WorkingHoursJson))
            {
                normalizedWorkingHours = WorkingHoursSerializer.Normalize(request.WorkingHoursJson);
                if (normalizedWorkingHours == null)
                {
                    return Result<ClinicDto>.Failure("Horaires de travail invalides.");
                }
            }

            // Handle logo upload if provided
            var originalLogoUrl = clinic.LogoUrl; // Persisted value, used for orphan cleanup below
            string? logoUrl = originalLogoUrl;    // Keep existing logo by default
            var logoContentType = clinic.LogoContentType;

            if (request.LogoFile != null)
            {
                // Same profile as the practitioner cachet — a logo is rendered into every document and served
                // back inline from the app's own origin. This path had no validation of any kind.
                var logoValidation = await FileUploadValidator.ValidateAsync(
                    FileUploadProfile.ProfileImage,
                    request.LogoFileName,
                    request.LogoLength,
                    request.LogoFile,
                    cancellationToken);

                if (logoValidation.IsFailure)
                {
                    return Result<ClinicDto>.Failure(logoValidation.Error!);
                }

                var logo = logoValidation.Value!;

                // Delete old logo if it exists
                if (!string.IsNullOrWhiteSpace(clinic.LogoUrl))
                {
                    try
                    {
                        await _fileStorage.DeleteAsync(clinic.LogoUrl, cancellationToken);
                    }
                    catch
                    {
                        // Log but don't fail if deletion fails
                    }
                }

                // US-5: the clinic segment is the storage's own, so the path here is relative to it.
                logoUrl = await _fileStorage.UploadAsync(
                    logo.Content,
                    logo.ContentType,
                    clinicId,
                    "logo",
                    cancellationToken);

                // The VALIDATED type, from the catalog entry the bytes agreed with — never the browser's claim.
                logoContentType = logo.ContentType;
            }

            try
            {
                // Update clinic information
                // Band A — each field is applied when the request MENTIONED it (even as empty, which clears) and
                // kept otherwise. `?? current` cannot express that: it makes a cleared field indistinguishable
                // from an omitted one, which is the defect.
                clinic.Update(
                    request.Name,
                    request.AddressSpecified ? request.Address : clinic.Address,
                    request.PhoneSpecified ? request.Phone : clinic.Phone,
                    request.EmailSpecified ? request.Email : clinic.Email,
                    logoUrl,
                    request.CitySpecified ? request.City : clinic.City,
                    logoContentType);

                clinic.SetBillingSettings(
                    request.MatriculeFiscalSpecified ? request.MatriculeFiscal : clinic.MatriculeFiscal,
                    request.VatApplicable ?? clinic.VatApplicable,
                    request.VatRate ?? clinic.VatRate,
                    request.StampDutyEnabled ?? clinic.StampDutyEnabled,
                    request.StampDutyAmount ?? clinic.StampDutyAmount);

                // Working hours (AC-7): only touched when a valid payload was supplied.
                if (normalizedWorkingHours != null)
                {
                    clinic.SetWorkingHours(normalizedWorkingHours);
                }

                // Validate the save against the version the USER was editing, not the one this
                // handler just loaded — that one always matches and would detect nothing.
                _unitOfWork.SetExpectedVersion(clinic, request.Version);
                await _clinicRepository.UpdateAsync(clinic, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // The save failed after we may have stored a new logo blob. Remove it only when the
                // persisted clinic won't reference it (a new key), so we never delete a logo the DB
                // still points to — the logo path is deterministic (FR-C3).
                if (!string.IsNullOrWhiteSpace(logoUrl) && logoUrl != originalLogoUrl)
                {
                    try { await _fileStorage.DeleteAsync(logoUrl, cancellationToken); }
                    catch { /* best-effort orphan cleanup: don't mask the original failure */ }
                }
                throw;
            }

            // Return updated clinic DTO
            var clinicDto = new ClinicDto
            {
                Id = clinic.Id,
                Name = clinic.Name,
                Address = clinic.Address,
                City = clinic.City,
                Phone = clinic.Phone,
                Email = clinic.Email,
                Code = clinic.Code,
                LogoUrl = clinic.LogoUrl,
                MatriculeFiscal = clinic.MatriculeFiscal,
                VatApplicable = clinic.VatApplicable,
                VatRate = clinic.VatRate,
                StampDutyEnabled = clinic.StampDutyEnabled,
                StampDutyAmount = clinic.StampDutyAmount,
                WorkingHours = WorkingHoursSerializer.Parse(clinic.WorkingHoursJson)
            };

            return Result<ClinicDto>.Success(clinicDto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error updating clinic");
            return Result<ClinicDto>.Failure("La mise à jour du cabinet a échoué.");
        }
    }

}

