using MediatR;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Queries;

/// <summary>One band of the cabinet's current letterhead, for the settings card and the document editor's preview.</summary>
public class GetClinicLetterheadBandQuery : IRequest<Result<ClinicLetterheadBandDto>>
{
    public LetterheadBandKind Band { get; set; }
}

public class ClinicLetterheadBandDto
{
    public Stream FileStream { get; set; } = null!;

    /// <summary>The storage key never changes under a band, so it is a strong validator for the browser cache.</summary>
    public string StorageKey { get; set; } = string.Empty;
}

public class GetClinicLetterheadBandQueryHandler
    : IRequestHandler<GetClinicLetterheadBandQuery, Result<ClinicLetterheadBandDto>>
{
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicRepository _clinicRepository;
    private readonly IFileStorage _fileStorage;

    public GetClinicLetterheadBandQueryHandler(
        ICurrentClinicResolver clinicResolver, IClinicRepository clinicRepository, IFileStorage fileStorage)
    {
        _clinicResolver = clinicResolver;
        _clinicRepository = clinicRepository;
        _fileStorage = fileStorage;
    }

    public async Task<Result<ClinicLetterheadBandDto>> Handle(
        GetClinicLetterheadBandQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicId = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicId.IsFailure)
            {
                return Result<ClinicLetterheadBandDto>.FailureFrom(clinicId);
            }

            var clinic = await _clinicRepository.GetByIdAsync(clinicId.Value, cancellationToken);
            var key = request.Band switch
            {
                LetterheadBandKind.Header => clinic?.LetterheadHeaderStorageKey,
                LetterheadBandKind.Footer => clinic?.LetterheadFooterStorageKey,
                _ => clinic?.LetterheadBodyStorageKey
            };
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new NotFoundException("Aucun en-tête n'est enregistré pour ce cabinet.");
            }

            var stream = await _fileStorage.DownloadAsync(key, cancellationToken);
            return Result<ClinicLetterheadBandDto>.Success(new ClinicLetterheadBandDto { FileStream = stream, StorageKey = key });
        }
        catch (Exception ex) when (ex is not ConflictException and not NotFoundException)
        {
            return Result<ClinicLetterheadBandDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
