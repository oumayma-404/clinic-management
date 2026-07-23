using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Queries;

/// <summary>Lists the clinic's recurring appointment series (active by default), with the count of linked appointments.</summary>
public class GetRecurringSeriesQuery : IRequest<Result<IEnumerable<RecurringAppointmentDto>>>
{
    public bool ActiveOnly { get; set; } = true;
}

public class GetRecurringSeriesQueryHandler : IRequestHandler<GetRecurringSeriesQuery, Result<IEnumerable<RecurringAppointmentDto>>>
{
    private readonly IRecurringAppointmentRepository _recurringRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetRecurringSeriesQueryHandler(
        IRecurringAppointmentRepository recurringRepository,
        IAppointmentRepository appointmentRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _recurringRepository = recurringRepository;
        _appointmentRepository = appointmentRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IEnumerable<RecurringAppointmentDto>>> Handle(GetRecurringSeriesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<IEnumerable<RecurringAppointmentDto>>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var series = await _recurringRepository.GetByClinicIdAsync(clinic.Value, request.ActiveOnly, cancellationToken);
            var appointments = await _appointmentRepository.GetByClinicIdAsync(clinic.Value, cancellationToken: cancellationToken);

            var countsBySeries = appointments
                .Where(a => a.RecurringAppointmentId.HasValue)
                .GroupBy(a => a.RecurringAppointmentId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var dtos = series.Select(s => s.ToDto(
                appointmentCount: countsBySeries.TryGetValue(s.Id, out var count) ? count : 0));

            return Result<IEnumerable<RecurringAppointmentDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<RecurringAppointmentDto>>.Failure($"Erreur lors de la récupération des séries : {ex.Message}");
        }
    }
}
