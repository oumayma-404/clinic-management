using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// One row of <see cref="IAppointmentRepository.GetStatusTimelineAsync"/>: when a séance was booked and what became
/// of it, and nothing else.
///
/// <para>Its own type rather than a tuple, for the reason <see cref="ProcedureMixRow"/> is: the two fields are
/// <c>DateTime</c> and an enum, so a positional tuple invites a caller to read them in the wrong order and nothing
/// would fail. Naming them makes the caller's fold read as what it is.</para>
///
/// <para><b><see cref="StartUtc"/> is UTC and must be converted before it is bucketed.</b> Every consumer of this
/// type puts it through <c>ClinicClock.ToClinicLocal</c> first — Tunisia is UTC+1, so a 00:30 local booking is
/// 23:30 UTC the previous day, and bucketing the raw instant moves it into yesterday's column. That is the whole
/// reason the shift is done here in C# rather than in SQL.</para>
/// </summary>
/// <param name="StartUtc">The slot's start instant, in UTC, exactly as stored.</param>
/// <param name="Status">The séance's status at the time of the read.</param>
public sealed record AppointmentStatusSlot(DateTime StartUtc, AppointmentStatus Status);
