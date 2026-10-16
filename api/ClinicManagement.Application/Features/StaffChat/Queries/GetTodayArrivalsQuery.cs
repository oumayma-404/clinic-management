using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.StaffChat.Commands;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.StaffChat.Queries;

/// <summary>
/// The arrival action for each of today's appointments, as this person would meet it (AC-3.3, AC-3.4): already
/// announced (when, by whom), or why it cannot be sent — the same rule the command applies, never a second copy.
/// </summary>
public class GetTodayArrivalsQuery : IRequest<Result<List<StaffArrivalDto>>>
{
}

public class GetTodayArrivalsQueryHandler : IRequestHandler<GetTodayArrivalsQuery, Result<List<StaffArrivalDto>>>
{
    private readonly IStaffMessageRepository _messages;
    private readonly IUserRepository _users;
    private readonly IDoctorRepository _doctors;
    private readonly IClinicContext _clinicContext;

    public GetTodayArrivalsQueryHandler(
        IStaffMessageRepository messages, IUserRepository users, IDoctorRepository doctors, IClinicContext clinicContext)
    {
        _messages = messages;
        _users = users;
        _doctors = doctors;
        _clinicContext = clinicContext;
    }

    public async Task<Result<List<StaffArrivalDto>>> Handle(GetTodayArrivalsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await StaffChatRoster.LoadForCallerAsync(_clinicContext, _users, _doctors, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result<List<StaffArrivalDto>>.FailureFrom(loaded);
            }

            var roster = loaded.Value!;
            var now = DateTime.UtcNow;
            var (from, to) = ClinicClock.TodayRangeUtc(now);
            var appointments = await _messages.GetAppointmentsBetweenAsync(roster.ClinicId, from, to, cancellationToken);
            var arrivals = (await _messages.GetArrivalsAsync(roster.ClinicId, from, to, cancellationToken))
                .ToDictionary(a => a.AppointmentId);

            return Result<List<StaffArrivalDto>>.Success(appointments
                .Where(a => a.PatientId is not null && a.Status != AppointmentStatus.Cancelled)
                .Select(a =>
                {
                    arrivals.TryGetValue(a.Id, out var sent);
                    return new StaffArrivalDto
                    {
                        AppointmentId = a.Id,
                        SentAt = sent?.SentAt,
                        SentBy = sent is null
                            ? null
                            : StaffChatInbox.PersonOf(sent.SenderUserId, roster),
                        Unavailable = ArrivalTarget.For(a, roster, now).Refusal?.Message,
                    };
                })
                .ToList());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<List<StaffArrivalDto>>.Failure("Impossible de lire les arrivées du jour.", ex);
        }
    }
}
