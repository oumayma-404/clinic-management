using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Commands;

/// <summary>
/// Stores the cabinet's letterhead — a header band, an optional footer band and, for « Page entière », the strip
/// between them; all PNG at true scale.
/// <para>
/// ⚠️ Each save writes under a NEW key and never deletes the band it replaces: medical documents snapshot the key
/// they were issued with, so an old ordonnance keeps the letterhead it was printed on.
/// </para>
/// </summary>
public class UpdateClinicLetterheadCommand : IRequest<Result<ClinicLetterheadDto>>
{
    public Stream Header { get; set; } = Stream.Null;
    public string? HeaderFileName { get; set; }
    public long HeaderLength { get; set; }

    /// <summary>Null = this letterhead has no footer band (any previous one is dropped, not kept).</summary>
    public Stream? Footer { get; set; }
    public string? FooterFileName { get; set; }
    public long FooterLength { get; set; }

    /// <summary>« Page entière »: the strip between the bands. Null = the bands alone (any previous strip is dropped).</summary>
    public Stream? Body { get; set; }
    public string? BodyFileName { get; set; }
    public long BodyLength { get; set; }

    /// <summary>The clinic row's version the client read; 0 skips the check.</summary>
    public uint Version { get; set; }
}

public class UpdateClinicLetterheadCommandHandler
    : IRequestHandler<UpdateClinicLetterheadCommand, Result<ClinicLetterheadDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateClinicLetterheadCommandHandler> _logger;

    public UpdateClinicLetterheadCommandHandler(
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ILogger<UpdateClinicLetterheadCommandHandler> logger)
    {
        _clinicRepository = clinicRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ClinicLetterheadDto>> Handle(
        UpdateClinicLetterheadCommand request, CancellationToken cancellationToken)
    {
        var written = new List<string>(2);
        try
        {
            var userId = _clinicContext.GetUserId();
            var user = string.IsNullOrEmpty(userId)
                ? null
                : await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<ClinicLetterheadDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            if (!user.IsAdmin())
            {
                return Result<ClinicLetterheadDto>.Failure(LetterheadRules.AdminOnly);
            }

            var clinic = await _clinicRepository.GetByIdAsync(user.ClinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<ClinicLetterheadDto>.Failure("Clinique introuvable.");
            }

            // Both bands are validated before either is stored, so a refused footer never leaves a header blob behind.
            var header = await LetterheadBandReader.ReadAsync(
                LetterheadBandKind.Header, request.Header, request.HeaderFileName, request.HeaderLength, cancellationToken);
            if (header.IsFailure)
            {
                return Result<ClinicLetterheadDto>.FailureFrom(header);
            }

            Result<byte[]>? footer = null;
            if (request.Footer != null)
            {
                footer = await LetterheadBandReader.ReadAsync(
                    LetterheadBandKind.Footer, request.Footer, request.FooterFileName, request.FooterLength, cancellationToken);
                if (footer.IsFailure)
                {
                    return Result<ClinicLetterheadDto>.FailureFrom(footer);
                }
            }

            Result<byte[]>? body = null;
            if (request.Body != null)
            {
                body = await LetterheadBandReader.ReadAsync(
                    LetterheadBandKind.Body, request.Body, request.BodyFileName, request.BodyLength, cancellationToken);
                if (body.IsFailure)
                {
                    return Result<ClinicLetterheadDto>.FailureFrom(body);
                }
            }

            var batch = Guid.NewGuid().ToString("N");
            var headerKey = await StoreAsync(header.Value!, clinic.Id, $"letterhead/{batch}/header", written, cancellationToken);
            var footerKey = footer == null
                ? null
                : await StoreAsync(footer.Value!, clinic.Id, $"letterhead/{batch}/footer", written, cancellationToken);
            var bodyKey = body == null
                ? null
                : await StoreAsync(body.Value!, clinic.Id, $"letterhead/{batch}/body", written, cancellationToken);

            clinic.SetLetterhead(headerKey, footerKey, bodyKey);
            _unitOfWork.SetExpectedVersion(clinic, request.Version);
            await _clinicRepository.UpdateAsync(clinic, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<ClinicLetterheadDto>.Success(ClinicLetterheadDto.From(clinic));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            await DeleteWrittenAsync(written);
            _logger.LogError(ex, "Error saving the clinic letterhead");
            return Result<ClinicLetterheadDto>.Failure("Erreur lors de l'enregistrement de l'en-tête.");
        }
        catch (ConflictException)
        {
            await DeleteWrittenAsync(written);
            throw;
        }
    }

    private async Task<string> StoreAsync(
        byte[] png, Guid clinicId, string relativePath, List<string> written, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream(png, writable: false);
        var key = await _fileStorage.UploadAsync(content, "image/png", clinicId, relativePath, cancellationToken);
        written.Add(key);
        return key;
    }

    // The save failed: nothing points at these new blobs, and nothing ever will.
    private async Task DeleteWrittenAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try { await _fileStorage.DeleteAsync(key, CancellationToken.None); }
            catch (Exception ex) { _logger.LogWarning(ex, "Orphaned letterhead blob {Key} could not be deleted", key); }
        }
    }
}
