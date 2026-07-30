using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

public class GetPatientsQuery : IRequest<Result<PagedResult<PatientDto>>>
{
    /// <summary>Optional free-text filter matched (case- and accent-insensitive) against first name, last
    /// name, the "first last" combination, and the phone number. When null/blank, all patients are returned.
    /// <para>Matched <b>in SQL</b> since paging was introduced — see <see cref="IPatientRepository"/>. It searches
    /// the whole clinic, never just the requested page: a search that only saw the page would answer a different
    /// question from the one the user typed.</para></summary>
    public string? SearchTerm { get; set; }

    /// <summary>Optional cap on the number of results returned (applied after filtering). Ignored when null or ≤ 0.</summary>
    public int? Limit { get; set; }

    /// <summary>
    /// 1-based page and page size. Both null = every matching row, which is what the header search, the patient
    /// pickers and the AI dispatcher rely on.
    /// </summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>
    /// Only patients carrying an active flag. Applied in SQL — it was a client-side filter over the full list,
    /// which a page turns into "the flagged ones on this page".
    /// </summary>
    public bool FlaggedOnly { get; set; }

    /// <summary>
    /// Optional inclusive bounds on the registration date, pushed into SQL. Added for the dashboard's « Nouveaux
    /// patients » drill-through: the KPI counts patients created in the period, so clicking it has to open the list
    /// filtered by the same window — otherwise the card shows 12 and the page shows every patient the clinic has.
    /// Archived patients stay excluded, matching the count.
    /// </summary>
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
}

public class GetPatientsQueryHandler : IRequestHandler<GetPatientsQuery, Result<PagedResult<PatientDto>>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;

    public GetPatientsQueryHandler(
        IPatientRepository patientRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext)
    {
        _patientRepository = patientRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<PagedResult<PatientDto>>> Handle(GetPatientsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Get user ID from token
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<PagedResult<PatientDto>>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<PagedResult<PatientDto>>.Failure("Utilisateur introuvable.");
            }

            var clinicId = user.ClinicId;

            // `Limit` predates paging and is what the header lookup asks for: "the first N matches, no pager".
            // It is folded into a page request rather than kept as a separate in-memory `Take`, so that read is
            // bounded in SQL too — a capped list that still materialised every patient of the clinic was the
            // same unbounded read wearing a cap.
            var paging = PageRequest.From(request.Page, request.PageSize)
                ?? (request.Limit is > 0 ? PageRequest.Of(1, request.Limit.Value) : null);

            // Archived patients are excluded: this backs both the patients page and the header search.
            // Filtering, searching, ordering and paging are all in SQL now. The accent-insensitive match that
            // used to force the search into memory is `unaccent()` on the database side (see SqlSearch) —
            // keeping it here would have meant searching only the page the user is already looking at.
            var page = await _patientRepository.GetByClinicIdAsync(
                clinicId,
                createdFrom: request.CreatedFrom,
                createdTo: request.CreatedTo,
                searchTerm: request.SearchTerm,
                flaggedOnly: request.FlaggedOnly,
                paging: paging,
                cancellationToken: cancellationToken);

            return Result<PagedResult<PatientDto>>.Success(page.Map(p => p.ToDto()));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PagedResult<PatientDto>>.Failure($"Error retrieving patients: {ex.Message}");
        }
    }
}
