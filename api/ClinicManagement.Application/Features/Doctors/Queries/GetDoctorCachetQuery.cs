using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Doctors.Queries;

/// <summary>
/// Streams a doctor's cachet image with its persisted content type. Any authenticated user in the doctor's
/// clinic may read it (it appears on that clinic's documents); cross-clinic / no-cachet reads as not found.
/// </summary>
public class GetDoctorCachetQuery : IRequest<Result<DoctorCachetDto>>
{
    public Guid DoctorId { get; set; }
}

public class GetDoctorCachetQueryHandler : IRequestHandler<GetDoctorCachetQuery, Result<DoctorCachetDto>>
{
    private readonly IDoctorRepository _doctorRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<GetDoctorCachetQueryHandler> _logger;

    public GetDoctorCachetQueryHandler(
        IDoctorRepository doctorRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IFileStorage fileStorage,
        ILogger<GetDoctorCachetQueryHandler> logger)
    {
        _doctorRepository = doctorRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<Result<DoctorCachetDto>> Handle(GetDoctorCachetQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<DoctorCachetDto>.Failure("Utilisateur non authentifié.");
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<DoctorCachetDto>.Failure("Utilisateur introuvable.");
            }

            var doctor = await _doctorRepository.GetByIdAsync(request.DoctorId, cancellationToken);
            if (doctor == null || doctor.ClinicId != user.ClinicId || doctor.CachetStorageKey == null)
            {
                return Result<DoctorCachetDto>.Failure("Cachet introuvable.");
            }

            var stream = await _fileStorage.DownloadAsync(doctor.CachetStorageKey, cancellationToken);
            return Result<DoctorCachetDto>.Success(new DoctorCachetDto
            {
                FileStream = stream,
                ContentType = doctor.CachetContentType ?? "application/octet-stream"
            });
        }
        catch (Exception ex)
        {
            // A stale key (row set, blob missing/unreadable) must degrade to a clean 404 like the sibling
            // render path — never an unhandled 500 (the controller maps this failure to 404).
            _logger.LogWarning(ex, "Failed to read cachet blob for doctor {DoctorId}", request.DoctorId);
            return Result<DoctorCachetDto>.Failure("Cachet introuvable.");
        }
    }
}
