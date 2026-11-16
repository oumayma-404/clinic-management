using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Backup.Commands;

/// <summary>
/// A clinic PC telling the server it copied the camera originals it keeps to the cabinet's archive disk
/// (<c>features/camera-photo-import</c>, storage « deux maisons »). The vault copy report's twin, per PC.
///
/// <para>⚠️ <b>Compared with the record, not believed</b> — <c>ReportVaultCopyCommand</c>'s rule: the originals never
/// reached the server, so this report is the only fact it will ever have about them. A copy covering fewer files or
/// bytes than the PC reported keeping is remembered and does not advance the stamp, and the alert stands.</para>
///
/// <para>⚠️ <b>Sent by the PAGE on the Windows app's behalf</b>, with the signed-in person's session: the shell's own
/// credential reaches nothing outside <c>[AcceptsScopedToken]</c>, and reception is usually who is signed in on the PC
/// the camera is plugged into. It lives under <c>Features/Backup</c> so it broadcasts nothing — a scheduled copy is
/// not something any open screen shows.</para>
/// </summary>
public class ReportPhotoOriginalsCopyCommand : IRequest<Result<bool>>
{
    /// <summary>The PC's name, as it reports keeping originals (<c>PatientFile.OriginalHeldBy</c>).</summary>
    public string HeldBy { get; set; } = string.Empty;

    public int FileCount { get; set; }

    public long TotalBytes { get; set; }
}

public class ReportPhotoOriginalsCopyCommandHandler : IRequestHandler<ReportPhotoOriginalsCopyCommand, Result<bool>>
{
    private readonly IPhotoOriginalsCopyRepository _copies;
    private readonly IPatientFileRepository _files;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly INotificationGenerator _notifications;
    private readonly ILogger<ReportPhotoOriginalsCopyCommandHandler> _logger;

    public ReportPhotoOriginalsCopyCommandHandler(
        IPhotoOriginalsCopyRepository copies,
        IPatientFileRepository files,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        INotificationGenerator notifications,
        ILogger<ReportPhotoOriginalsCopyCommandHandler> logger)
    {
        _copies = copies;
        _files = files;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(ReportPhotoOriginalsCopyCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var holder = request.HeldBy?.Trim() ?? string.Empty;
            if (holder.Length == 0 || holder.Length > PatientFile.MaxOriginalHolderLength
                || request.FileCount < 0 || request.TotalBytes < 0)
            {
                return Result<bool>.Failure("Le rapport de copie est incomplet.");
            }

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<bool>.Failure(clinic.Error ?? "Unable to resolve current clinic");
            }

            var expected = (await _files.GetCameraOriginalTotalsAsync(clinic.Value, cancellationToken))
                .FirstOrDefault(t => string.Equals(t.HeldBy, holder, StringComparison.Ordinal));
            // « At least », not « exactly »: the archive folder is the practice's own and may hold more than the app put
            // there. A PC holding nothing on record is trivially covered.
            var covered = expected is null
                || (request.FileCount >= expected.Files && request.TotalBytes >= expected.Bytes);

            var now = DateTime.UtcNow;
            var copy = await _copies.GetAsync(clinic.Value, holder, cancellationToken);
            if (copy is null)
            {
                copy = PhotoOriginalsCopy.Start(Guid.NewGuid(), clinic.Value, holder);
                await _copies.AddAsync(copy, cancellationToken);
            }

            copy.RecordReport(request.FileCount, request.TotalBytes, covered, now);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (!covered)
            {
                _logger.LogWarning(
                    "Clinic {ClinicId}: a photo-originals copy of {FileCount} file(s) / {TotalBytes} bytes fell short of "
                    + "the {ExpectedFiles} file(s) / {ExpectedBytes} bytes on record; the stamp is NOT advanced.",
                    clinic.Value, request.FileCount, request.TotalBytes, expected!.Files, expected.Bytes);
            }

            // Post-commit and best-effort, in its own guard: the report is recorded whatever the bell's read does, and
            // answering a failure here would make the page send a report that is already on record again.
            try
            {
                var stale = await PhotoOriginalsCopyCheck.FindStaleAsync(_files, _copies, clinic.Value, now, cancellationToken);
                await PhotoOriginalsCopyCheck.ApplyAsync(_notifications, clinic.Value, stale, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Clinic {ClinicId}: the photo-originals copy report was recorded, but the alert was not re-evaluated.",
                    clinic.Value);
            }

            return Result<bool>.Success(covered);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error recording a photo-originals copy report");
            return Result<bool>.Failure("Erreur lors de l'enregistrement de la copie des photos originales.");
        }
    }
}
