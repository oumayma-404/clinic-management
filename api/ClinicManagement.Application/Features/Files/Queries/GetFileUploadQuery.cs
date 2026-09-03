using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Queries;

/// <summary>
/// Where an upload got to — <b>the read that makes resuming possible</b>.
///
/// <para>A browser that was interrupted knows what it was sending and nothing about what arrived: the last part
/// it wrote may have been stored, or lost with the response. Asking is the only honest way to find out, and it
/// is what turns « start again » into « carry on from part 34 ».</para>
///
/// <para>⚠️ An expired session is reported as <b>gone</b> rather than returned with its counts, because its
/// staging area has been reclaimed: answering « you were at part 34 » about parts that no longer exist would
/// send a client to build a file out of nothing.</para>
/// </summary>
public class GetFileUploadQuery : IRequest<Result<FileUploadSessionDto>>
{
    public Guid PatientId { get; set; }
    public Guid UploadId { get; set; }
}

public class GetFileUploadQueryHandler : IRequestHandler<GetFileUploadQuery, Result<FileUploadSessionDto>>
{
    private readonly IFileUploadSessionRepository _sessions;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetFileUploadQueryHandler(
        IFileUploadSessionRepository sessions,
        ICurrentClinicResolver clinicResolver)
    {
        _sessions = sessions;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<FileUploadSessionDto>> Handle(
        GetFileUploadQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<FileUploadSessionDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var session = await _sessions.GetByIdAsync(request.UploadId, cancellationToken);
            if (session == null || session.ClinicId != clinicResult.Value || session.PatientId != request.PatientId)
            {
                return Result<FileUploadSessionDto>.Failure("Envoi introuvable.");
            }

            if (session.HasExpired(DateTime.UtcNow))
            {
                return Result<FileUploadSessionDto>.Failure(UploadFileChunkCommandHandler.ExpiredMessage);
            }

            return Result<FileUploadSessionDto>.Success(session.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<FileUploadSessionDto>.Failure("Erreur lors de la lecture de l'envoi.");
        }
    }
}
