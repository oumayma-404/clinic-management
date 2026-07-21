using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
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
                    catch { /* best-effort blob cleanup; a leaked blob is preferable to failing the update */ }
                }
            }
            else if (request.CachetStream != null)
            {
                if (string.IsNullOrWhiteSpace(request.CachetContentType))
                {
                    return Result<DoctorProfileDto>.Failure("Le type du fichier cachet est requis.");
                }

                // Deterministic per-doctor key → re-upload overwrites in place. Persist the real content
                // type (unlike the clinic-logo path, which hardcodes image/png).
                var key = $"{doctor.ClinicId}/doctors/{doctor.Id}/cachet";
                var storageKey = await _fileStorage.UploadAsync(request.CachetStream, request.CachetContentType, key, cancellationToken);
                doctor.SetCachet(storageKey, request.CachetContentType);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating doctor profile {DoctorId}", request.DoctorId);
            return Result<DoctorProfileDto>.Failure("Erreur lors de la mise à jour du profil praticien.");
        }
    }
}
