using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Backup.Archive;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Backup.Queries;

/// <summary>
/// Builds the cabinet's own archive — every clinical and financial record it holds, plus the blobs behind them,
/// as one file it keeps on its own PC (<c>clinic-data-archive-and-restore</c>).
///
/// <para><b>A query, and a real one.</b> It writes nothing, and being one keeps it off
/// <c>RealtimeBroadcastBehavior</c>, which derives its key from the namespace — a « Backup » broadcast on every
/// download would tell every open browser in the practice that something changed when nothing did. (The area is
/// excluded anyway; the placement means it stays right if that list moves.)</para>
///
/// <para><b>It is scoped to the caller's own clinic and to nothing else</b> (AC-1). The clinic is resolved from
/// the DB user record, exactly as every other read in the product does it, and the store selects rows on an
/// explicit clinic predicate rather than trusting the ambient query filter — this is the one read whose miss would
/// put another cabinet's patients in a file the practice keeps on a laptop.</para>
/// </summary>
public class BuildClinicArchiveQuery : IRequest<Result<ClinicArchiveFile>>
{
}

/// <summary>
/// The archive as a readable stream, with the name and the manifest the response needs.
///
/// <para><b>A <see cref="Stream"/> and not a <c>byte[]</c>.</b> The whole file used to be built into a
/// <c>MemoryStream</c> and then copied <i>again</i> by <c>ToArray()</c> — about twice the archive in contiguous
/// large-object-heap allocations before the response held any of it, against a configured ceiling of a gigabyte
/// and a handler whose own comment anticipates « twenty years of radiographs ». Those are the sizes that take a
/// shared hosted backend down and every other cabinet's requests with it, and a <c>byte[]</c> over 2 GB throws
/// outright — so AC-8's « a cabinet must always be able to take its data out » failed for exactly the practices
/// with the most to lose. The caller disposes it; a temp file deletes itself on close.</para>
/// </summary>
public sealed record ClinicArchiveFile(Stream Content, string FileName, ClinicArchiveManifest Manifest);

public class BuildClinicArchiveQueryHandler
    : IRequestHandler<BuildClinicArchiveQuery, Result<ClinicArchiveFile>>
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicArchiveStore _store;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<BuildClinicArchiveQueryHandler> _logger;

    public BuildClinicArchiveQueryHandler(
        IUserRepository userRepository,
        IClinicRepository clinicRepository,
        IClinicContext clinicContext,
        IClinicArchiveStore store,
        IFileStorage fileStorage,
        ILogger<BuildClinicArchiveQueryHandler> logger)
    {
        _userRepository = userRepository;
        _clinicRepository = clinicRepository;
        _clinicContext = clinicContext;
        _store = store;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<Result<ClinicArchiveFile>> Handle(
        BuildClinicArchiveQuery request, CancellationToken cancellationToken)
    {
        var callerId = _clinicContext.GetUserId();
        if (string.IsNullOrEmpty(callerId))
        {
            return Result<ClinicArchiveFile>.Failure("Session invalide, veuillez vous reconnecter.");
        }

        var caller = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
        if (caller == null)
        {
            return Result<ClinicArchiveFile>.Failure("Utilisateur introuvable.");
        }

        // Defense in depth behind the controller's AdminOnly policy, the shape BackupNowCommand already uses.
        // An archive is every record the cabinet holds, in one file, unencrypted.
        if (!caller.IsAdmin())
        {
            return Result<ClinicArchiveFile>.Failure(
                "Seuls les administrateurs peuvent télécharger une archive du cabinet.");
        }

        var clinic = await _clinicRepository.GetByIdAsync(caller.ClinicId, cancellationToken);
        if (clinic == null)
        {
            return Result<ClinicArchiveFile>.Failure("Cabinet introuvable.");
        }

        // Buffered rather than written straight to the response, and to a TEMP FILE rather than to RAM.
        //
        // Buffered because ZipArchive in Create mode seeks back to write each entry's directory record, and an
        // HTTP response body is forward-only — and a failure half way through a streamed download would deliver a
        // truncated file with a 200 beside it, the one outcome a backup must never produce.
        //
        // ⚠️ That argument is for *somewhere to seek*, not for the heap. A cabinet's rows are megabytes but its
        // radiographs are not, and on the hosted deployment one process serves every practice: the previous
        // MemoryStream, plus a `ToArray()` copy of it, put roughly twice the archive on the large-object heap
        // before a byte reached the client. `DeleteOnClose` means the file is gone when the response is disposed,
        // including when the request is abandoned.
        var buffer = new FileStream(
            Path.Combine(Path.GetTempPath(), $"clinic-archive-{Guid.NewGuid():N}.zip"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);

        try
        {
            var manifest = await ClinicArchivePackager.WriteAsync(
                buffer, clinic.Id, clinic.Name, _store, _fileStorage, _logger, cancellationToken);

            _logger.LogInformation(
                "Archive built for clinic {ClinicId}: {Tables} tables, {Blobs} blobs, {Bytes} bytes.",
                clinic.Id, manifest.Tables.Count, manifest.BlobCount, buffer.Length);

            buffer.Position = 0;

            return Result<ClinicArchiveFile>.Success(new ClinicArchiveFile(
                buffer,
                ClinicArchiveFormat.FileName(clinic.Name, ClinicClock.ClinicToday()),
                manifest));
        }
        catch
        {
            await buffer.DisposeAsync();
            throw;
        }
    }
}
