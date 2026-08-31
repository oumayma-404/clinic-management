using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Backup.Commands;

/// <summary>
/// The shell telling the server that a copy of this cabinet's coffre reached a second place
/// (<c>clinic-file-vault</c>).
///
/// <para>⚠️ <b>It exists because the server cannot see the practice's disk.</b> A coffre original was never
/// uploaded, so nothing here can observe whether it is safe — and dental imaging carries a ten-to-twenty-year
/// retention duty. This report is the only channel through which « la pratique en a une deuxième copie » exists as
/// a fact on the server, which is what lets the daily pass nag when it stops arriving.</para>
///
/// <para>⚠️ <b>It lives under <c>Features/Backup</c> deliberately.</b> That area is on
/// <c>RealtimeResourceResolver.ExcludedAreas</c>; a new <c>Features/&lt;Area&gt;</c> folder would emit a realtime
/// key <c>web/lib/realtime/clinic-hub.ts</c> must then declare, and a broadcast on every scheduled copy would
/// tell every open browser something changed when nothing a user can see did.</para>
/// </summary>
public class ReportVaultCopyCommand : IRequest<Result<bool>>
{
    /// <summary>How many originals the copy covered. Zero is legitimate — an empty coffre copies to nothing.</summary>
    public int FileCount { get; set; }

    public long TotalBytes { get; set; }
}

public class ReportVaultCopyCommandHandler : IRequestHandler<ReportVaultCopyCommand, Result<bool>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly INotificationGenerator _notifications;
    private readonly ILogger<ReportVaultCopyCommandHandler> _logger;

    public ReportVaultCopyCommandHandler(
        IClinicRepository clinicRepository,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        INotificationGenerator notifications,
        ILogger<ReportVaultCopyCommandHandler> logger)
    {
        _clinicRepository = clinicRepository;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(ReportVaultCopyCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<bool>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var clinic = await _clinicRepository.GetByIdAsync(clinicResult.Value, cancellationToken);
            if (clinic == null)
            {
                return Result<bool>.Failure("Cabinet introuvable.");
            }

            // The shell's own clock is not trusted for this: a machine with a wrong date could park the stamp in
            // the future and silence the alert for ever. The server records when it was TOLD.
            clinic.MarkVaultCopied(DateTime.UtcNow);
            await _clinicRepository.UpdateAsync(clinic, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Clinic {ClinicId} reported a vault copy of {FileCount} file(s), {TotalBytes} bytes",
                clinic.Id, request.FileCount, request.TotalBytes);

            // Post-commit and best-effort, like every other generator call: the copy really happened, and a feed
            // write failing must not make the shell believe it did not.
            await _notifications.ClearVaultCopyStaleAsync(clinic.Id, cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error recording a vault copy report");
            return Result<bool>.Failure("Erreur lors de l'enregistrement de la copie du coffre.");
        }
    }
}
