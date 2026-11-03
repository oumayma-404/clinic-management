using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Dashboard.Queries;

/// <summary>« Premiers pas » for the caller's cabinet. Administrators only (the controller's policy, re-checked here).</summary>
public class GetGettingStartedQuery : IRequest<Result<GettingStartedDto>>;

/// <summary>
/// Reads the seven facts « Premiers pas » ticks from.
///
/// <para>⚠️ <b>The age check comes before any count.</b> After its first month a cabinet is never shown the card
/// again, and the dashboard asks on every load — so for every cabinet that is not new this costs one row.</para>
///
/// <para>The reads are sequential: they share the request's <c>DbContext</c> (<c>GetDashboardQuery</c>'s reason).
/// The clinic comes from the caller's own account row, never from the request.</para>
/// </summary>
public class GetGettingStartedQueryHandler : IRequestHandler<GetGettingStartedQuery, Result<GettingStartedDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly ISecondFactorPolicy _secondFactorPolicy;
    private readonly ILogger<GetGettingStartedQueryHandler> _logger;

    public GetGettingStartedQueryHandler(
        IClinicContext clinicContext,
        IUserRepository userRepository,
        IClinicRepository clinicRepository,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        IDentalRecordRepository dentalRecordRepository,
        ISecondFactorPolicy secondFactorPolicy,
        ILogger<GetGettingStartedQueryHandler> logger)
    {
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _clinicRepository = clinicRepository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _dentalRecordRepository = dentalRecordRepository;
        _secondFactorPolicy = secondFactorPolicy;
        _logger = logger;
    }

    public async Task<Result<GettingStartedDto>> Handle(GetGettingStartedQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Result<GettingStartedDto>.Failure(ErrorMessages.Generic);
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user is null)
            {
                return Result<GettingStartedDto>.Failure(ErrorMessages.Generic);
            }

            // The setup of the cabinet is the administrator's job; anyone else is shown nothing, not refused.
            if (!user.IsAdmin())
            {
                return Result<GettingStartedDto>.Success(GettingStartedDto.Hidden);
            }

            var clinicId = user.ClinicId;
            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            if (clinic is null)
            {
                return Result<GettingStartedDto>.Failure(ErrorMessages.Generic);
            }

            if (!GettingStartedRules.IsWithinFirstDays(clinic.CreatedAt, DateTime.UtcNow))
            {
                return Result<GettingStartedDto>.Success(GettingStartedDto.Hidden);
            }

            var staff = (await _userRepository.GetByClinicIdAsync(clinicId, cancellationToken: cancellationToken))
                .Items
                .Where(member => member.IsActive)
                .ToList();
            var patients = await _patientRepository.CountByClinicIdAsync(clinicId, cancellationToken);
            var appointments = await _appointmentRepository.CountByClinicIdAsync(
                clinicId, cancellationToken: cancellationToken);
            var hasDentalRecord = await _dentalRecordRepository.AnyForClinicAsync(clinicId, cancellationToken);

            var facts = new GettingStartedFacts(
                SecurityCodeSet: user.IsTotpEnrolled,
                ActiveAccounts: staff.Count,
                PhoneAppUsed: user.PhoneAppFirstSignInAt is not null,
                HasPatient: patients > 0,
                HasAppointment: appointments > 0,
                HasDentalRecord: hasDentalRecord,
                HasLetterhead: !string.IsNullOrEmpty(clinic.LetterheadHeaderStorageKey));

            var steps = GettingStartedRules.Steps(facts, _secondFactorPolicy.RequiresAdminSecondFactor);
            if (steps.All(step => step.Done))
            {
                return Result<GettingStartedDto>.Success(GettingStartedDto.Hidden);
            }

            var phoneApp = staff
                .Select(member => new PhoneAppPersonDto(
                    member.FullName ?? member.Email ?? string.Empty,
                    member.Role,
                    member.PhoneAppPlatform,
                    member.PhoneAppFirstSignInAt,
                    IsYou: member.Id == user.Id))
                .ToList();

            return Result<GettingStartedDto>.Success(new GettingStartedDto(true, steps, phoneApp));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Reading « Premiers pas » failed.");
            return Result<GettingStartedDto>.Failure(ErrorMessages.Generic);
        }
    }
}
