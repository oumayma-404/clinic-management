using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Queries;

/// <summary>
/// « Traitements en cours » — every act the cabinet has started and not finished, with the next step and whether
/// a séance is booked for it.
/// <para>
/// ⚠️ It lives under <c>Features/TreatmentPlans</c> and not in a folder of its own. That is mechanical, not
/// stylistic: <c>RealtimeResourceResolver</c> derives the broadcast key from the namespace, so a
/// <c>Features/Treatments</c> folder would emit <c>treatments</c> — a key <c>clinic-hub.ts</c> does not declare —
/// and <c>RealtimeResourceResolverTests</c> fails the build in both directions. It is also honest: what this
/// reads *is* devis data. The visit-closure worklist stayed under <c>Features/Appointments</c> for the same
/// reason.
/// </para>
/// <para>
/// Paged, like every list read. Asking for page 1 of size 1 and reading <c>TotalCount</c> is how the journée's
/// chip gets its number without a second endpoint — the total is exact regardless of the page.
/// </para>
/// </summary>
public class GetTreatmentsInProgressQuery : IRequest<Result<PagedResult<TreatmentInProgressDto>>>
{
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>
    /// Free text over the patient's name and the devis number, matched in SQL across the whole clinic. Blank
    /// leaves the list untouched. Deliberately the same field the devis list searches, because « Traitements »
    /// asks one question of both halves: « where is this patient's treatment, and what did we agree? »
    /// </summary>
    public string? Search { get; set; }
}

public class GetTreatmentsInProgressQueryHandler
    : IRequestHandler<GetTreatmentsInProgressQuery, Result<PagedResult<TreatmentInProgressDto>>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetTreatmentsInProgressQueryHandler> _logger;

    public GetTreatmentsInProgressQueryHandler(
        ITreatmentPlanRepository planRepository,
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetTreatmentsInProgressQueryHandler> logger)
    {
        _planRepository = planRepository;
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<PagedResult<TreatmentInProgressDto>>> Handle(
        GetTreatmentsInProgressQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PagedResult<TreatmentInProgressDto>>.FailureFrom(clinicResult);
            }

            var page = await TreatmentsInProgressReader.ReadAsync(
                clinicResult.Value,
                PageRequest.From(request.Page, request.PageSize),
                _planRepository,
                _appointmentRepository,
                _patientRepository,
                cancellationToken,
                request.Search);

            return Result<PagedResult<TreatmentInProgressDto>>.Success(page);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading treatments in progress");
            return Result<PagedResult<TreatmentInProgressDto>>.Failure(
                "Erreur lors du chargement des traitements en cours.");
        }
    }
}
