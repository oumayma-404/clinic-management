using System.Text.Json.Serialization;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Files;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Application.Features.Cameras.Commands;

// The four writes of the camera registry (features/camera-photo-import). One file because they share their
// refusals and their doctor check; each is still its own request and handler, as everywhere else.

/// <summary>
/// Pairs a camera with the cabinet — or returns the one already paired. Upsert by fingerprint, because the import
/// screen sends this every time it meets a camera and cannot know whether another PC paired it a minute ago.
/// </summary>
public class RegisterClinicCameraCommand : IRequest<Result<ClinicCameraDto>>
{
    public string Fingerprint { get; set; } = string.Empty;
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? SerialSuffix { get; set; }

    /// <summary>The practitioner whose agenda this camera's photos belong to — only applied when the camera is new.</summary>
    public Guid? DoctorId { get; set; }
}

/// <summary>The settings card's edit. Every field is tri-state: omitted = unchanged.</summary>
public class UpdateClinicCameraCommand : IRequest<Result<ClinicCameraDto>>
{
    [JsonIgnore] public Guid CameraId { get; set; }

    public string? Label { get; set; }

    private Guid? _doctorId;

    /// <summary>Omitted = unchanged; an explicit null = « tous les praticiens ».</summary>
    public Guid? DoctorId
    {
        get => _doctorId;
        set { _doctorId = value; DoctorIdSpecified = true; }
    }

    [JsonIgnore] public bool DoctorIdSpecified { get; private set; }

    public int? ClockOffsetMinutes { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// What « Ranger » tells the registry: the latest shot just filed (so the next import starts after it) and, when the
/// screen corrected the camera's clock, the correction to remember. A machine report — no version is checked.
/// </summary>
public class RecordCameraImportCommand : IRequest<Result<ClinicCameraDto>>
{
    [JsonIgnore] public Guid CameraId { get; set; }

    /// <summary>The latest capture time filed, clinic wall-clock (already corrected).</summary>
    public DateTime? LatestCapturedAtLocal { get; set; }

    public int? ClockOffsetMinutes { get; set; }
}

public class ForgetClinicCameraCommand : IRequest<Result>
{
    public Guid CameraId { get; set; }
}

internal static class CameraDoctor
{
    /// <summary>Null when the doctor is absent or this cabinet's; the refusal otherwise.</summary>
    public static async Task<Result?> CheckAsync(
        IDoctorRepository doctors, Guid clinicId, Guid? doctorId, CancellationToken cancellationToken)
    {
        if (doctorId is null || doctorId == Guid.Empty)
        {
            return null;
        }

        var doctor = await doctors.GetByIdAsync(doctorId.Value, cancellationToken);
        return doctor is not null && doctor.ClinicId == clinicId
            ? null
            : Result.Failure(CameraImportRefusals.CameraDoctorUnknown(), CameraImportRefusals.CameraDoctorUnknownCode);
    }
}

public class RegisterClinicCameraCommandHandler : IRequestHandler<RegisterClinicCameraCommand, Result<ClinicCameraDto>>
{
    private readonly IClinicCameraRepository _cameras;
    private readonly IDoctorRepository _doctors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public RegisterClinicCameraCommandHandler(
        IClinicCameraRepository cameras,
        IDoctorRepository doctors,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver)
    {
        _cameras = cameras;
        _doctors = doctors;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<ClinicCameraDto>> Handle(RegisterClinicCameraCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (!PhotoCaptureFacts.IsCaptureKey(request.Fingerprint)
                || (string.IsNullOrWhiteSpace(request.Make) && string.IsNullOrWhiteSpace(request.Model)))
            {
                return Result<ClinicCameraDto>.Failure(
                    CameraImportRefusals.InvalidKey(), CameraImportRefusals.InvalidKeyCode);
            }

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<ClinicCameraDto>.Failure(clinic.Error ?? "Unable to resolve current clinic");
            }

            var fingerprint = request.Fingerprint.Trim().ToLowerInvariant();
            var existing = await _cameras.GetByFingerprintAsync(clinic.Value, fingerprint, cancellationToken);
            if (existing is not null)
            {
                return Result<ClinicCameraDto>.Success(ClinicCameraDto.From(existing));
            }

            var refusal = await CameraDoctor.CheckAsync(_doctors, clinic.Value, request.DoctorId, cancellationToken);
            if (refusal is not null)
            {
                return Result<ClinicCameraDto>.FailureFrom(refusal);
            }

            var camera = ClinicCamera.Register(
                Guid.NewGuid(), clinic.Value, fingerprint, request.Make, request.Model, request.SerialSuffix, DateTime.UtcNow);
            camera.AssignDoctor(request.DoctorId);

            try
            {
                await _cameras.AddAsync(camera, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (UniqueViolation.Is(ex))
            {
                // Another PC paired the same body a moment ago; theirs is the camera.
                _unitOfWork.StopTracking(camera);
                var winner = await _cameras.GetByFingerprintAsync(clinic.Value, fingerprint, cancellationToken);
                if (winner is not null)
                {
                    return Result<ClinicCameraDto>.Success(ClinicCameraDto.From(winner));
                }

                throw;
            }

            return Result<ClinicCameraDto>.Success(ClinicCameraDto.From(camera));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<ClinicCameraDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

public class UpdateClinicCameraCommandHandler : IRequestHandler<UpdateClinicCameraCommand, Result<ClinicCameraDto>>
{
    private readonly IClinicCameraRepository _cameras;
    private readonly IDoctorRepository _doctors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public UpdateClinicCameraCommandHandler(
        IClinicCameraRepository cameras,
        IDoctorRepository doctors,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver)
    {
        _cameras = cameras;
        _doctors = doctors;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<ClinicCameraDto>> Handle(UpdateClinicCameraCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<ClinicCameraDto>.Failure(clinic.Error ?? "Unable to resolve current clinic");
            }

            var camera = await _cameras.GetByIdAsync(request.CameraId, cancellationToken);
            if (camera is null || camera.ClinicId != clinic.Value)
            {
                return Result<ClinicCameraDto>.Failure(
                    CameraImportRefusals.CameraNotFound(), CameraImportRefusals.CameraNotFoundCode);
            }

            if (request.DoctorIdSpecified)
            {
                var refusal = await CameraDoctor.CheckAsync(_doctors, clinic.Value, request.DoctorId, cancellationToken);
                if (refusal is not null)
                {
                    return Result<ClinicCameraDto>.FailureFrom(refusal);
                }
            }

            if (request.Label is not null
                && (request.Label.Trim().Length == 0 || request.Label.Trim().Length > ClinicCamera.MaxLabelLength))
            {
                return Result<ClinicCameraDto>.Failure(
                    $"Le nom de l'appareil doit faire entre 1 et {ClinicCamera.MaxLabelLength} caractères.");
            }

            if (request.ClockOffsetMinutes is { } minutes && Math.Abs(minutes) > ClinicCamera.MaxClockOffsetMinutes)
            {
                return Result<ClinicCameraDto>.Failure(
                    "Un décalage d'horloge de plus d'un jour ne se corrige pas : réglez l'heure de l'appareil.");
            }

            _unitOfWork.SetExpectedVersion(camera, request.Version);

            if (request.Label is not null)
            {
                camera.Rename(request.Label);
            }

            if (request.DoctorIdSpecified)
            {
                camera.AssignDoctor(request.DoctorId);
            }

            if (request.ClockOffsetMinutes is { } offset)
            {
                camera.SetClockOffset(offset);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<ClinicCameraDto>.Success(ClinicCameraDto.From(camera));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<ClinicCameraDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

public class RecordCameraImportCommandHandler : IRequestHandler<RecordCameraImportCommand, Result<ClinicCameraDto>>
{
    private readonly IClinicCameraRepository _cameras;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public RecordCameraImportCommandHandler(
        IClinicCameraRepository cameras,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver)
    {
        _cameras = cameras;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<ClinicCameraDto>> Handle(RecordCameraImportCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<ClinicCameraDto>.Failure(clinic.Error ?? "Unable to resolve current clinic");
            }

            var camera = await _cameras.GetByIdAsync(request.CameraId, cancellationToken);
            if (camera is null || camera.ClinicId != clinic.Value)
            {
                return Result<ClinicCameraDto>.Failure(
                    CameraImportRefusals.CameraNotFound(), CameraImportRefusals.CameraNotFoundCode);
            }

            var changed = false;

            // Clamped like a capture time: a « latest shot » in the future would hide every photo taken before it.
            var latest = request.LatestCapturedAtLocal is { } local
                ? PhotoCaptureFacts.Plausible(
                    ClinicClock.ToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified)), DateTime.UtcNow)
                : null;
            if (latest is { } instant)
            {
                changed |= camera.RecordImport(instant);
            }

            if (request.ClockOffsetMinutes is { } offset
                && Math.Abs(offset) <= ClinicCamera.MaxClockOffsetMinutes
                && offset != camera.ClockOffsetMinutes)
            {
                camera.SetClockOffset(offset);
                changed = true;
            }

            if (changed)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<ClinicCameraDto>.Success(ClinicCameraDto.From(camera));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<ClinicCameraDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

public class ForgetClinicCameraCommandHandler : IRequestHandler<ForgetClinicCameraCommand, Result>
{
    private readonly IClinicCameraRepository _cameras;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public ForgetClinicCameraCommandHandler(
        IClinicCameraRepository cameras,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver)
    {
        _cameras = cameras;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result> Handle(ForgetClinicCameraCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result.Failure(clinic.Error ?? "Unable to resolve current clinic");
            }

            var camera = await _cameras.GetByIdAsync(request.CameraId, cancellationToken);
            if (camera is null || camera.ClinicId != clinic.Value)
            {
                // Already forgotten: the outcome the caller asked for.
                return Result.Success();
            }

            // Photos keep their CameraModel snapshot: forgetting a camera erases nothing the record says.
            await _cameras.DeleteAsync(camera, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result.Failure(ErrorMessages.Generic, ex);
        }
    }
}
