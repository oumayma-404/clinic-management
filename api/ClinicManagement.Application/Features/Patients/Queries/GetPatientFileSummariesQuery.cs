using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

/// <summary>
/// The « Fichiers » directory — one page of the clinic's patients with the size of each one's file drawer.
///
/// <para><b>Why it is its own read and not a field on <see cref="GetPatientsQuery"/>.</b> Three aggregates per
/// row are a cost every caller of the patients list would pay — the header lookup, the pickers, the appointment
/// dialog, the CSV export — for a figure only one screen renders. And this read is ordered and filtered <i>by</i>
/// those aggregates, which is the half a shared DTO field could not give: « les patients qui ont des fichiers »
/// and « le plus de fichiers d'abord » are decisions taken before a page is cut.</para>
///
/// <para>It is a <b>query</b>, so it emits no realtime broadcast; the screen listens on the existing
/// <c>files</c> and <c>patients</c> keys, which the upload and patient commands already publish.</para>
/// </summary>
public class GetPatientFileSummariesQuery : IRequest<Result<PagedResult<PatientFileSummaryDto>>>
{
    /// <summary>
    /// Free-text over name and phone, matched in SQL across the whole clinic — never re-filter the returned
    /// rows, or the search silently narrows to the page the user is already looking at.
    /// </summary>
    public string? SearchTerm { get; set; }

    /// <summary>Only patients who actually have at least one file. Applied in SQL, before the page is cut.</summary>
    public bool WithFilesOnly { get; set; }

    /// <summary>
    /// How the page is ordered. Unrecognised values fall back to <see cref="PatientFileSummarySort.Name"/>
    /// rather than refusing: a stale bookmark or a hand-edited URL should show the directory, not a French
    /// error — the same reasoning <see cref="PageRequest"/> clamps rather than rejects.
    /// </summary>
    public PatientFileSummarySort Sort { get; set; } = PatientFileSummarySort.Name;

    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public class GetPatientFileSummariesQueryHandler
    : IRequestHandler<GetPatientFileSummariesQuery, Result<PagedResult<PatientFileSummaryDto>>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetPatientFileSummariesQueryHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<PagedResult<PatientFileSummaryDto>>> Handle(
        GetPatientFileSummariesQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PagedResult<PatientFileSummaryDto>>.Failure(
                    clinicResult.Error ?? "Session invalide, veuillez vous reconnecter.");
            }

            var page = await _patientRepository.GetFileSummariesAsync(
                clinicResult.Value,
                searchTerm: request.SearchTerm,
                withFilesOnly: request.WithFilesOnly,
                sort: request.Sort,
                paging: PageRequest.From(request.Page, request.PageSize),
                cancellationToken: cancellationToken);

            return Result<PagedResult<PatientFileSummaryDto>>.Success(page.Map(row => new PatientFileSummaryDto
            {
                PatientId = row.PatientId,
                FirstName = row.FirstName,
                LastName = row.LastName,
                PhoneNumber = row.PhoneNumber,
                FileCount = row.FileCount,
                TotalBytes = row.TotalBytes,
                LastUploadedAt = row.LastUploadedAtUtc,
            }));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PagedResult<PatientFileSummaryDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
