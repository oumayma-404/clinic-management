using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Files;
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

    /// <summary>The uploaded file's own name — the format is keyed on its extension, not on the sent header.</summary>
    public string? CachetFileName { get; set; }
    public long CachetLength { get; set; }
    public bool RemoveCachet { get; set; }
    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the user was
    /// editing; <c>0</c> means « not supplied » and skips the check (see <c>IUnitOfWork.SetExpectedVersion</c>).
    /// </summary>
    public uint Version { get; set; }
}

public class UpdateDoctorProfileCommandHandler : IRequestHandler<UpdateDoctorProfileCommand, Result<DoctorProfileDto>>
{
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

            // Key of a cachet blob the replacement leaves behind; dropped only after the update commits.
            string? supersededCachetKey = null;

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
                // The cachet is served back inline at the app origin and read fully into memory on every
                // document render (PdfGenerationService.LoadCachetImageAsync), so it is raster-only and small —
                // which is exactly what FileUploadProfile.ProfileImage says, in one place, for the logo too.
                var validation = await FileUploadValidator.ValidateAsync(
                    FileUploadProfile.ProfileImage,
                    request.CachetFileName,
                    request.CachetLength,
                    request.CachetStream,
                    cancellationToken);

                if (validation.IsFailure)
                {
                    return Result<DoctorProfileDto>.Failure(validation.Error!);
                }

                var cachet = validation.Value!;

                // Deterministic per-doctor key → re-upload overwrites in place. Persist the validated content
                // type (unlike the clinic-logo path, which hardcodes image/png). US-5: the clinic segment is
                // the storage's own, so the path here is relative to it.
                supersededCachetKey = doctor.CachetStorageKey;
                var storageKey = await _fileStorage.UploadAsync(
                    cachet.Content, cachet.ContentType, doctor.ClinicId, $"doctors/{doctor.Id}/cachet", cancellationToken);
                doctor.SetCachet(storageKey, cachet.ContentType);

                // Overwriting in place used to make this unnecessary. US-5 changed the key format, so a cachet
                // stored under the old one is a real blob nothing points at any more — delete it, once.
                if (supersededCachetKey == storageKey)
                {
                    supersededCachetKey = null;
                }
            }

            // Band B — validated against the copy the USER was editing, not the row this handler just read.
            _unitOfWork.SetExpectedVersion(doctor, request.Version);

            _doctorRepository.Update(doctor);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (supersededCachetKey != null)
            {
                try { await _fileStorage.DeleteAsync(supersededCachetKey, cancellationToken); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Best-effort superseded cachet blob delete failed for {Key}", supersededCachetKey);
                }
            }

            return Result<DoctorProfileDto>.Success(new DoctorProfileDto
            {
                Id = doctor.Id,
                FullName = doctor.FullName,
                Specialty = doctor.Specialty,
                OrdreNumberCnomdt = doctor.OrdreNumberCnomdt,
                HasCachet = doctor.CachetStorageKey != null,
                CachetContentType = doctor.CachetContentType,
                Version = doctor.Version
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error updating doctor profile {DoctorId}", request.DoctorId);
            return Result<DoctorProfileDto>.Failure("Erreur lors de la mise à jour du profil praticien.");
        }
    }
}
