using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using MediatR;

namespace ClinicManagement.Application.Features.Platform.Queries;

/// <summary>
/// The host's off-site copies, judged (<c>server-loss-recovery</c> Part 3) — the console's status strip and the
/// daily alert job's single read.
///
/// <para>⚠️ <b>Reads no cabinet.</b> A status file and PostgreSQL's archiver statistics are facts about the
/// deployment, so this records nothing in the access ledger (a read that touches no practice has nothing to
/// attribute) and goes through no tenant filter.</para>
/// </summary>
public class GetPlatformBackupHealthQuery : IRequest<Result<PlatformBackupHealthDto>>
{
}

public class GetPlatformBackupHealthQueryHandler
    : IRequestHandler<GetPlatformBackupHealthQuery, Result<PlatformBackupHealthDto>>
{
    private readonly IHostBackupStatusReader _reader;

    public GetPlatformBackupHealthQueryHandler(IHostBackupStatusReader reader)
    {
        _reader = reader;
    }

    public async Task<Result<PlatformBackupHealthDto>> Handle(
        GetPlatformBackupHealthQuery request, CancellationToken cancellationToken)
    {
        // The reader never throws (see its contract), so there is no failure branch: an unreadable record is a
        // verdict — « En retard » — and never an error page in place of the one screen that reports the problem.
        var snapshot = await _reader.ReadAsync(cancellationToken);
        return Result<PlatformBackupHealthDto>.Success(BackupHealthRules.Evaluate(snapshot, DateTime.UtcNow));
    }
}
