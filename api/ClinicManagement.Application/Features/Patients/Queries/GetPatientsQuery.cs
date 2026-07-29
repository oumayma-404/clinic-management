using System.Globalization;
using System.Text;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

public class GetPatientsQuery : IRequest<Result<IEnumerable<PatientDto>>>
{
    /// <summary>Optional free-text filter matched (case- and accent-insensitive) against first name, last
    /// name, the "first last" combination, and the phone number. When null/blank, all patients are returned.</summary>
    public string? SearchTerm { get; set; }

    /// <summary>Optional cap on the number of results returned (applied after filtering). Ignored when null or ≤ 0.</summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Optional inclusive bounds on the registration date, pushed into SQL. Added for the dashboard's « Nouveaux
    /// patients » drill-through: the KPI counts patients created in the period, so clicking it has to open the list
    /// filtered by the same window — otherwise the card shows 12 and the page shows every patient the clinic has.
    /// Archived patients stay excluded, matching the count.
    /// </summary>
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
}

public class GetPatientsQueryHandler : IRequestHandler<GetPatientsQuery, Result<IEnumerable<PatientDto>>>
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

    public async Task<Result<IEnumerable<PatientDto>>> Handle(GetPatientsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Get user ID from token
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<IEnumerable<PatientDto>>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<IEnumerable<PatientDto>>.Failure("Utilisateur introuvable.");
            }

            var clinicId = user.ClinicId;

            // Archived patients are excluded: this backs both the patients page and the header search.
            // The created-date bounds are applied in SQL by the repository, not in the in-memory filter below —
            // the search term has to be normalised in memory (accent-insensitivity), a date range does not.
            IEnumerable<Patient> patients = await _patientRepository.GetByClinicIdAsync(
                clinicId,
                createdFrom: request.CreatedFrom,
                createdTo: request.CreatedTo,
                cancellationToken: cancellationToken);

            // Server-side filter: match first/last/full name and phone, case- and accent-insensitive.
            var normalizedTerm = NormalizeForSearch(request.SearchTerm);
            if (!string.IsNullOrEmpty(normalizedTerm))
            {
                patients = patients.Where(p =>
                    NormalizeForSearch(p.FirstName).Contains(normalizedTerm) ||
                    NormalizeForSearch(p.LastName).Contains(normalizedTerm) ||
                    NormalizeForSearch($"{p.FirstName} {p.LastName}").Contains(normalizedTerm) ||
                    // Null-safe: this runs in memory over every patient in the clinic, so a single
                    // contact-less patient used to take out the whole list AND the header search with a 500.
                    NormalizeForSearch(p.PhoneNumber?.Value).Contains(normalizedTerm));
            }

            // Stable order so a capped result is deterministic.
            patients = patients.OrderBy(p => p.LastName).ThenBy(p => p.FirstName);

            if (request.Limit is > 0)
            {
                patients = patients.Take(request.Limit.Value);
            }

            var dtos = patients.Select(p => p.ToDto()).ToList();

            return Result<IEnumerable<PatientDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<PatientDto>>.Failure($"Error retrieving patients: {ex.Message}");
        }
    }

    // Lowercases and strips diacritics so "amine" matches "Amïne" (accent-insensitive) without a Postgres
    // unaccent extension. Runs in memory over the clinic's already-loaded patient list.
    private static string NormalizeForSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
