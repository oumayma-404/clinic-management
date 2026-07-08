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
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public Stream? LogoFile { get; set; }
    public string? LogoContentType { get; set; }
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
                return Result<ClinicDto>.Failure("User ID not found in token");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<ClinicDto>.Failure("User not found");
            }

            var clinicId = user.ClinicId;

            // Get clinic from database
            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<ClinicDto>.Failure("Clinic not found");
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
                    logoUrl);

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
                Phone = clinic.Phone,
                Email = clinic.Email,
                Code = clinic.Code,
                LogoUrl = clinic.LogoUrl
            };

            return Result<ClinicDto>.Success(clinicDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating clinic");
            return Result<ClinicDto>.Failure("Error updating clinic.");
        }
    }
}

