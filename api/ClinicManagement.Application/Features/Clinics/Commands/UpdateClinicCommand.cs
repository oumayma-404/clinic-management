using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Commands;

public class UpdateClinicCommand : IRequest<Result<ClinicDto>>
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public Stream? LogoFile { get; set; }
    public string? LogoContentType { get; set; }

    // Billing / note-d'honoraires settings. Null = leave the current value unchanged.
    public string? MatriculeFiscal { get; set; }
    public bool? VatApplicable { get; set; }
    public decimal? VatRate { get; set; }
    public bool? StampDutyEnabled { get; set; }
    public decimal? StampDutyAmount { get; set; }

    // TTN « El Fatoora » e-invoicing settings (null = leave the current value unchanged).
    public bool? TtnEInvoicingEnabled { get; set; }
    public string? TtnEnvironment { get; set; }

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

            // FR-8/US-6: changing the TTN e-invoicing settings is admin-only. Non-admins may still edit the
            // rest of the clinic/billing card as long as they don't alter the TTN toggle/environment.
            var desiredTtnEnabled = request.TtnEInvoicingEnabled ?? clinic.TtnEInvoicingEnabled;
            var desiredTtnEnvironment = request.TtnEnvironment ?? clinic.TtnEnvironment;
            var ttnSettingsChanging = desiredTtnEnabled != clinic.TtnEInvoicingEnabled
                || !string.Equals(desiredTtnEnvironment, clinic.TtnEnvironment, StringComparison.OrdinalIgnoreCase);
            if (ttnSettingsChanging && !user.IsAdmin())
            {
                return Result<ClinicDto>.Failure("Seul un administrateur peut modifier les paramètres de facturation électronique.");
            }

            // Audit § 2, finding 7: PUT /api/clinics carried NO role policy, so a secretary could change the
            // clinic's legal billing identity — matricule fiscal, TVA applicable/rate, timbre fiscal. Those
            // values are frozen onto every invoice issued afterwards, which makes them a different class of
            // setting from the phone number.
            //
            // Gated per FIELD rather than by closing the endpoint, extending the desired-vs-current pattern
            // the TTN check above already uses. That matters for a real reason: the settings form submits the
            // whole card, so a secretary correcting the clinic phone re-sends matricule fiscal and TVA at
            // their existing values. Comparing against the stored value means an unchanged field is not a
            // change, and only an actual edit is refused (spec EC-11).
            var legalBillingChanging =
                IsChanging(request.MatriculeFiscal, clinic.MatriculeFiscal)
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

            if (request.LogoFile != null && !string.IsNullOrWhiteSpace(request.LogoContentType))
            {
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

                // Upload new logo with org-id/logo path
                var logoPath = $"{clinicId}/logo";
                logoUrl = await _fileStorage.UploadAsync(
                    request.LogoFile,
                    request.LogoContentType,
                    logoPath,
                    cancellationToken);
            }

            try
            {
                // Update clinic information
                clinic.Update(
                    request.Name,
                    request.Address,
                    request.Phone,
                    request.Email,
                    logoUrl,
                    request.City ?? clinic.City);

                // Billing settings: apply provided values, keeping the current value where a field is null.
                clinic.SetBillingSettings(
                    request.MatriculeFiscal ?? clinic.MatriculeFiscal,
                    request.VatApplicable ?? clinic.VatApplicable,
                    request.VatRate ?? clinic.VatRate,
                    request.StampDutyEnabled ?? clinic.StampDutyEnabled,
                    request.StampDutyAmount ?? clinic.StampDutyAmount);

                // TTN e-invoicing settings: apply provided values, keeping the current value where null.
                clinic.SetElFatooraSettings(
                    request.TtnEInvoicingEnabled ?? clinic.TtnEInvoicingEnabled,
                    request.TtnEnvironment ?? clinic.TtnEnvironment);

                // Working hours (AC-7): only touched when a valid payload was supplied.
                if (normalizedWorkingHours != null)
                {
                    clinic.SetWorkingHours(normalizedWorkingHours);
                }

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
                TtnEInvoicingEnabled = clinic.TtnEInvoicingEnabled,
                TtnEnvironment = clinic.TtnEnvironment,
                WorkingHours = WorkingHoursSerializer.Parse(clinic.WorkingHoursJson)
            };

            return Result<ClinicDto>.Success(clinicDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating clinic");
            return Result<ClinicDto>.Failure("Erreur lors de la mise à jour de la clinique.");
        }
    }

    /// <summary>
    /// True only when an <b>optional string</b> field is present in the request AND differs from what is
    /// stored. An omitted field (null) means "leave it alone", so it is never treated as an edit — that is what
    /// lets a non-admin submit the whole settings form without tripping the admin gate (spec EC-11).
    /// </summary>
    private static bool IsChanging(string? requested, string? current) =>
        requested is not null && !string.Equals(requested, current, StringComparison.Ordinal);
}

