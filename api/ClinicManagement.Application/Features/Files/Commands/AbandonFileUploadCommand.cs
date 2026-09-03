using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Commands;

/// <summary>
/// Gives up an upload and releases its staging area.
///
/// <para>⚠️ <b>An upload that is already gone is a success.</b> This is called from a « Annuler » button, and the
/// expiry sweep may have reclaimed the same session a moment earlier — reporting « introuvable » to somebody who
/// asked for exactly the state they are now in would be a refusal with nothing behind it.</para>
///
/// <para>⚠️ <b>The parts are released before the row.</b> The row is how the sweep finds an orphaned staging
/// area, so deleting it first and then failing would leave bytes nothing can ever reach.</para>
/// </summary>
public class AbandonFileUploadCommand : IRequest<Result<bool>>
{
    public Guid PatientId { get; set; }
    public Guid UploadId { get; set; }
}

public class AbandonFileUploadCommandHandler : IRequestHandler<AbandonFileUploadCommand, Result<bool>>
{
    private readonly IFileUploadSessionRepository _sessions;
    private readonly IResumableUploadStore _uploadStore;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<AbandonFileUploadCommandHandler> _logger;

    public AbandonFileUploadCommandHandler(
        IFileUploadSessionRepository sessions,
        IResumableUploadStore uploadStore,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        ILogger<AbandonFileUploadCommandHandler> logger)
    {
        _sessions = sessions;
        _uploadStore = uploadStore;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(AbandonFileUploadCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<bool>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var session = await _sessions.GetByIdAsync(request.UploadId, cancellationToken);
            if (session == null || session.ClinicId != clinicResult.Value || session.PatientId != request.PatientId)
            {
                return Result<bool>.Success(true);
            }

            await _uploadStore.AbortAsync(session.ClinicId, session.StorageReference, cancellationToken);

            await _sessions.RemoveAsync(session, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error abandoning upload {Upload}", request.UploadId);
            return Result<bool>.Failure("Erreur lors de l'abandon de l'envoi.");
        }
    }
}
