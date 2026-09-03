using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Commands;

/// <summary>
/// One chunk of a resumable upload.
///
/// <para>⚠️ <b>Three refusals, and each one is a corrupt file it prevents.</b> A part out of order would splice a
/// hole into the middle of a radiograph; a part of the wrong length would do the same, silently, because nothing
/// downstream re-measures a staged part; and a first chunk whose header does not match the declared format is the
/// same renamed-file refusal an ordinary upload makes, just arriving one request later.</para>
///
/// <para>⚠️ <b>Re-sending the part already accepted is a success, not an error.</b> A client whose response was
/// lost cannot tell « stored » from « never arrived » — and answering « that is where you are » lets it carry on
/// rather than start again, which is the whole feature.</para>
/// </summary>
public class UploadFileChunkCommand : IRequest<Result<FileUploadSessionDto>>, IDoesNotBroadcast
{
    public Guid PatientId { get; set; }
    public Guid UploadId { get; set; }
    public int PartNumber { get; set; }
    public long Length { get; set; }
    public Stream Content { get; set; } = null!;
}

public class UploadFileChunkCommandHandler
    : IRequestHandler<UploadFileChunkCommand, Result<FileUploadSessionDto>>
{
    public const string OutOfOrderMessage =
        "Ce morceau n'est pas celui attendu. Reprenez l'envoi : l'application vous dira où il en était.";

    public const string WrongLengthMessage =
        "Ce morceau n'a pas la taille attendue ; il a probablement été interrompu. Renvoyez-le.";

    public const string ExpiredMessage =
        "Cet envoi a expiré et ses morceaux ont été libérés. Recommencez-le.";

    private readonly IFileUploadSessionRepository _sessions;
    private readonly IResumableUploadStore _uploadStore;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<UploadFileChunkCommandHandler> _logger;

    public UploadFileChunkCommandHandler(
        IFileUploadSessionRepository sessions,
        IResumableUploadStore uploadStore,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        ILogger<UploadFileChunkCommandHandler> logger)
    {
        _sessions = sessions;
        _uploadStore = uploadStore;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<FileUploadSessionDto>> Handle(
        UploadFileChunkCommand request, CancellationToken cancellationToken)
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
                return Result<FileUploadSessionDto>.Failure(ExpiredMessage);
            }

            // Already stored: answer with where the upload stands rather than refusing a client that simply lost
            // our last response.
            if (request.PartNumber == session.ReceivedParts)
            {
                return Result<FileUploadSessionDto>.Success(session.ToDto());
            }

            if (request.PartNumber != session.NextPart || request.PartNumber > session.TotalParts)
            {
                return Result<FileUploadSessionDto>.Failure(OutOfOrderMessage);
            }

            if (request.Length != session.ExpectedPartLength(request.PartNumber))
            {
                return Result<FileUploadSessionDto>.Failure(WrongLengthMessage);
            }

            var content = request.Content;

            // ⚠️ The signature is checked on the FIRST chunk, because that is where the header is. The declared
            // length passed is the whole file's, not the chunk's, so the size refusal stays the one the session
            // was opened under; the validator hands the stream back rewound, header included.
            if (request.PartNumber == 1)
            {
                var validation = await FileUploadValidator.ValidateAsync(
                    FileUploadProfile.PatientFile,
                    session.FileName,
                    session.DeclaredLength,
                    content,
                    cancellationToken);

                if (validation.IsFailure)
                {
                    return Result<FileUploadSessionDto>.FailureFrom(validation);
                }

                content = validation.Value!.Content;
            }

            await _uploadStore.WritePartAsync(
                session.ClinicId, session.StorageReference, request.PartNumber, content, request.Length,
                cancellationToken);

            session.AcceptPart(request.PartNumber, request.Length);
            session.KeepAlive(DateTime.UtcNow);

            await _sessions.UpdateAsync(session, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<FileUploadSessionDto>.Success(session.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error storing part {Part} of upload {Upload}", request.PartNumber, request.UploadId);
            return Result<FileUploadSessionDto>.Failure("Erreur lors de l'envoi de ce morceau.");
        }
    }
}
