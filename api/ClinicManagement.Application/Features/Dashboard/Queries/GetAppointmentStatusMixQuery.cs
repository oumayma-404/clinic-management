using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Dashboard.Readers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Dashboard.Queries;

/// <summary>
/// « Rendez-vous par statut » for one window.
///
/// <para><b>Why this is its own query and not a field on <see cref="GetDashboardQuery"/>.</b> The card carries its
/// own period control, so its window is not the page's — and a card that can be re-scoped on its own cannot be
/// served by a response whose window is decided elsewhere. Everything else on the dashboard still arrives in the one
/// <c>GET /api/dashboard</c> call; only this card has a second window, so only this card has a second read.</para>
///
/// <para>The bounds are <b>day keys</b> (<c>YYYY-MM-DD</c>), not instants. See
/// <see cref="AppointmentStatusWindow"/> for why, and for what building them in the browser cost la caisse.</para>
/// </summary>
public class GetAppointmentStatusMixQuery : IRequest<Result<AppointmentStatusMixDto>>
{
    /// <summary>Inclusive first clinic-local day. Both bounds omitted ⇒ the current clinic-local week.</summary>
    public string? From { get; set; }

    /// <summary>Inclusive last clinic-local day.</summary>
    public string? To { get; set; }

    /// <summary>Narrows to one practitioner's own séances; null is the whole cabinet.</summary>
    public Guid? DoctorId { get; set; }
}

public class GetAppointmentStatusMixQueryHandler
    : IRequestHandler<GetAppointmentStatusMixQuery, Result<AppointmentStatusMixDto>>
{
    private readonly IDashboardAppointmentStatusReader _reader;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetAppointmentStatusMixQueryHandler> _logger;

    public GetAppointmentStatusMixQueryHandler(
        IDashboardAppointmentStatusReader reader,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetAppointmentStatusMixQueryHandler> logger)
    {
        _reader = reader;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<AppointmentStatusMixDto>> Handle(
        GetAppointmentStatusMixQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<AppointmentStatusMixDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            // The window is resolved BEFORE the clinic is read from, so an unreadable range or one over the cap is
            // a refusal the user can act on rather than a query that runs first and is thrown away.
            var windowResult = AppointmentStatusWindow.Resolve(request.From, request.To, DateTime.UtcNow);
            if (windowResult.IsFailure)
            {
                return Result<AppointmentStatusMixDto>.Failure(windowResult.Error!);
            }

            var dto = await _reader.ReadAsync(
                clinicResult.Value, windowResult.Value!, request.DoctorId, cancellationToken);

            return Result<AppointmentStatusMixDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Unhandled failure reading the appointment status mix");
            return Result<AppointmentStatusMixDto>.Failure(
                "Erreur lors du chargement des rendez-vous par statut. Veuillez réessayer.");
        }
    }
}
