using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Backup.Commands;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Backup.Queries;

/// <summary>
/// One entry of the cabinet-wide file manifest as it reaches a caller (<c>patient-file-mirror</c>).
///
/// <para>⚠️ It carries <b>no storage key and no clinic id</b>, the same two omissions
/// <see cref="ClinicManagement.Application.DTOs.PatientFileDto"/> makes and for the same reason.</para>
/// </summary>
public sealed record ClinicFileManifestEntryDto(
    Guid FileId,
    Guid PatientId,
    string PatientName,
    string FileName,
    string ContentType,
    long FileSize,
    DateTime UploadedAt);

/// <summary>
/// Every file this cabinet holds, so a machine keeping a browsable copy can work out what it is missing
/// (<c>patient-file-mirror</c>).
///
/// <para>⚠️ <b>It lives in <c>Features/Backup</c> and not in a new area on purpose.</b> A new
/// <c>Features/&lt;Area&gt;</c> folder emits a realtime resource key that
/// <c>web/lib/realtime/clinic-hub.ts</c> must then declare, and <c>RealtimeResourceResolverTests</c> compares the
/// two sets in both directions. This read has no screen and nothing to broadcast; it belongs beside the archive
/// it complements.</para>
///
/// <para>⚠️ <b>Admin-only, like the archive and unlike the per-patient file list.</b> Reading one patient's
/// documents is reception's daily work (<c>AnyClinicRole</c>); enumerating the whole cabinet's holdings in one
/// call is the same class of read as taking the archive out, and is gated the same way.</para>
/// </summary>
public class GetClinicFileManifestQuery : IRequest<Result<PagedResult<ClinicFileManifestEntryDto>>>
{
    public int? Page { get; set; }

    public int? PageSize { get; set; }
}

public class GetClinicFileManifestQueryHandler
    : IRequestHandler<GetClinicFileManifestQuery, Result<PagedResult<ClinicFileManifestEntryDto>>>
{
    private readonly IUserRepository _users;
    private readonly IClinicContext _clinicContext;
    private readonly IPatientFileRepository _files;

    public GetClinicFileManifestQueryHandler(
        IUserRepository users,
        IClinicContext clinicContext,
        IPatientFileRepository files)
    {
        _users = users;
        _clinicContext = clinicContext;
        _files = files;
    }

    public async Task<Result<PagedResult<ClinicFileManifestEntryDto>>> Handle(
        GetClinicFileManifestQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // The same resolver the grant handlers use: the clinic comes from the account row, not from the
            // token's claim, and a non-admin is refused here rather than by the ambient filter returning nothing.
            var clinic = await ArchiveGrantGuard.ResolveAdminClinicAsync(_clinicContext, _users, cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<PagedResult<ClinicFileManifestEntryDto>>.Failure(clinic.Error!);
            }

            var page = await _files.GetClinicManifestPageAsync(
                clinic.Value, PageRequest.From(request.Page, request.PageSize), cancellationToken);

            return Result<PagedResult<ClinicFileManifestEntryDto>>.Success(page.Map(Map));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PagedResult<ClinicFileManifestEntryDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }

    private static ClinicFileManifestEntryDto Map(ClinicFileManifestRow row) => new(
        row.FileId,
        row.PatientId,
        row.PatientName,
        row.FileName,
        row.ContentType,
        row.FileSize,
        row.UploadedAt);
}
