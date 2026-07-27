using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Doctors.Commands;

/// <summary>
/// Sets a practitioner's CNOMDT order number and cachet image (FR-2.5 / FR-3.1). Editing is restricted to
/// the practitioner's own record OR an admin (own-or-admin, enforced in-handler). <see cref="DoctorId"/>
/// null targets the caller's own doctor record (<c>PUT /api/doctors/me</c>); a value targets a specific
/// doctor (<c>PUT /api/doctors/{id}</c>, admin or self). The cachet content type is persisted alongside the
/// storage key so the image is served back with the right MIME type.
/// </summary>
public class UpdateDoctorProfileCommand : IRequest<Result<DoctorProfileDto>>
{
    public Guid? DoctorId { get; set; }
    public string? OrdreNumberCnomdt { get; set; }
    public Stream? CachetStream { get; set; }
    public string? CachetContentType { get; set; }
    public bool RemoveCachet { get; set; }
}

public class UpdateDoctorProfileCommandHandler : IRequestHandler<UpdateDoctorProfileCommand, Result<DoctorProfileDto>>
{
    // The cachet is embedded into every generated PDF and read fully into memory each render — keep it small.
    private const int MaxCachetBytes = 2 * 1024 * 1024; // 2 MB

    private readonly IDoctorRepository _doctorRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateDoctorProfileCommandHandler> _logger;

    public UpdateDoctorProfileCommandHandler(
        IDoctorRepository doctorRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ILogger<UpdateDoctorProfileCommandHandler> logger)
    {
        _doctorRepository = doctorRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<DoctorProfileDto>> Handle(UpdateDoctorProfileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<DoctorProfileDto>.Failure("Utilisateur non authentifié.");
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<DoctorProfileDto>.Failure("Utilisateur introuvable.");
            }

            Doctor? doctor;
            if (request.DoctorId.HasValue)
            {
                doctor = await _doctorRepository.GetByIdAsync(request.DoctorId.Value, cancellationToken);
            }
            else
            {
                doctor = await _doctorRepository.GetByUserIdAsync(userId, cancellationToken);
                if (doctor == null)
                {
                    return Result<DoctorProfileDto>.Failure("Aucun profil praticien n'est associé à votre compte.");
                }
            }

            // Cross-clinic (or missing) targets read as "not found" (tenant isolation).
            if (doctor == null || doctor.ClinicId != user.ClinicId)
            {
                return Result<DoctorProfileDto>.Failure("Praticien introuvable.");
            }

            // FR-3.1: own-or-admin. Checked BEFORE any mutation / upload so an unauthorized caller never
            // touches storage.
            var isOwnRecord = doctor.UserId != null && doctor.UserId == user.Id;
            if (!user.IsAdmin() && !isOwnRecord)
            {
                return Result<DoctorProfileDto>.Failure("Vous ne pouvez modifier que votre propre profil.");
            }

            doctor.SetOrdreNumber(request.OrdreNumberCnomdt);

            if (request.RemoveCachet)
            {
                var previousKey = doctor.CachetStorageKey;
                doctor.RemoveCachet();
                if (previousKey != null)
                {
                    try { await _fileStorage.DeleteAsync(previousKey, cancellationToken); }
                    catch (Exception ex)
                    {
                        // Best-effort cleanup — a leaked blob is preferable to failing the update — but log
                        // it so a persistently failing delete is diagnosable (never swallow silently).
                        _logger.LogWarning(ex, "Best-effort cachet blob delete failed for {Key}", previousKey);
                    }
                }
            }
            else if (request.CachetStream != null)
            {
                if (string.IsNullOrWhiteSpace(request.CachetContentType))
                {
                    return Result<DoctorProfileDto>.Failure("Le type du fichier cachet est requis.");
                }

                // FR-3.1 security: the cachet is served back inline at the app origin, so accept only raster
                // image types — reject image/svg+xml, text/html, etc. (which could execute script).
                var declaredType = request.CachetContentType.Trim().ToLowerInvariant();
                if (declaredType != "image/png" && declaredType != "image/jpeg" && declaredType != "image/jpg")
                {
                    return Result<DoctorProfileDto>.Failure("Le cachet doit être une image PNG ou JPEG.");
                }

                // Buffer under a hard size cap: the blob is read fully into memory on every document render
                // (PdfGenerationService.LoadCachetImageAsync), so an oversized "image" must be rejected up front.
                using var buffer = new MemoryStream();
                await request.CachetStream.CopyToAsync(buffer, cancellationToken);
                if (buffer.Length == 0)
                {
                    return Result<DoctorProfileDto>.Failure("Le fichier cachet est vide.");
                }
                if (buffer.Length > MaxCachetBytes)
                {
                    return Result<DoctorProfileDto>.Failure("Le cachet est trop volumineux (2 Mo maximum).");
                }

                // Content-type headers are trivially spoofable — verify the bytes actually start with a
                // PNG/JPEG magic signature before trusting (and persisting) the declared type.
                var bytes = buffer.ToArray();
                if (!IsPng(bytes) && !IsJpeg(bytes))
                {
                    return Result<DoctorProfileDto>.Failure("Le fichier cachet n'est pas une image PNG ou JPEG valide.");
                }

                var contentType = declaredType == "image/jpg" ? "image/jpeg" : declaredType;

                // Deterministic per-doctor key → re-upload overwrites in place. Persist the validated content
                // type (unlike the clinic-logo path, which hardcodes image/png).
                buffer.Position = 0;
                var key = $"{doctor.ClinicId}/doctors/{doctor.Id}/cachet";
                var storageKey = await _fileStorage.UploadAsync(buffer, contentType, key, cancellationToken);
                doctor.SetCachet(storageKey, contentType);
            }

            _doctorRepository.Update(doctor);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<DoctorProfileDto>.Success(new DoctorProfileDto
            {
                Id = doctor.Id,
                FullName = doctor.FullName,
                Specialty = doctor.Specialty,
                OrdreNumberCnomdt = doctor.OrdreNumberCnomdt,
                HasCachet = doctor.CachetStorageKey != null,
                CachetContentType = doctor.CachetContentType
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error updating doctor profile {DoctorId}", request.DoctorId);
            return Result<DoctorProfileDto>.Failure("Erreur lors de la mise à jour du profil praticien.");
        }
    }

    // Leading magic bytes — the authoritative signal for the image type (content-type headers are spoofable).
    private static bool IsPng(byte[] b) =>
        b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
        && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;

    private static bool IsJpeg(byte[] b) =>
        b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
}
