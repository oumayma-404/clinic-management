using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Application.Features.Appointments.Queries;

/// <summary>Lists the clinic's recurring appointment series (active by default), with the count of linked appointments.</summary>
public class GetRecurringSeriesQuery : IRequest<Result<PagedResult<RecurringAppointmentDto>>>
{
    public bool ActiveOnly { get; set; } = true;

    /// <summary>1-based page and page size. Both null = every matching row.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>Free-text filter, matched in SQL across the whole clinic — never only the requested page.</summary>
    public string? SearchTerm { get; set; }
}

public class GetRecurringSeriesQueryHandler : IRequestHandler<GetRecurringSeriesQuery, Result<PagedResult<RecurringAppointmentDto>>>
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

    public async Task<Result<PagedResult<RecurringAppointmentDto>>> Handle(GetRecurringSeriesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<PagedResult<RecurringAppointmentDto>>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var series = await _recurringRepository.GetByClinicIdAsync(
                clinic.Value,
                request.ActiveOnly,
                request.SearchTerm,
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            // Counted with one GROUP BY over the series ON THIS PAGE. It used to read EVERY appointment of the
            // clinic and group them in memory, which would have made paging the series pointless — the page got
            // smaller, the read behind it did not.
            var countsBySeries = await _appointmentRepository.CountByRecurringSeriesAsync(
                clinic.Value,
                series.Items.Select(s => s.Id).ToList(),
                cancellationToken);

            var dtos = series.Map(s => s.ToDto(
                appointmentCount: countsBySeries.TryGetValue(s.Id, out var count) ? count : 0));

            return Result<PagedResult<RecurringAppointmentDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PagedResult<RecurringAppointmentDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
