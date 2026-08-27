using ClinicManagement.Application.Common;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>
/// « Rendez-vous par statut »: the chosen window's séances, bucketed by clinic-local day / week / month and folded
/// into the five <see cref="AppointmentStatusClass"/> classes.
///
/// <para><b>Two reads, deliberately of different shapes.</b> The buckets need one row per appointment, because the
/// clinic-local shift that decides which column a séance lands in cannot be expressed in SQL against a
/// <c>timestamptz</c> (see <see cref="IAppointmentRepository.GetStatusTimelineAsync"/>). The comparison figure needs
/// only a number, so it takes the indexed <c>GROUP BY</c> that already exists. Widening the row-level read to cover
/// both windows would have doubled the rows transferred to produce one integer.</para>
///
/// <para><b>Zero-filled, never gap-omitted.</b> Every bucket the window contains is emitted, including the ones with
/// nothing in them — <c>AppointmentStatusWindow.Buckets</c> owns that rule and the note there explains what omitting
/// them costs.</para>
/// </summary>
public class DashboardAppointmentStatusReader : IDashboardAppointmentStatusReader
{
    private readonly IAppointmentRepository _appointmentRepository;

    public DashboardAppointmentStatusReader(IAppointmentRepository appointmentRepository)
    {
        _appointmentRepository = appointmentRepository;
    }

    public async Task<AppointmentStatusMixDto> ReadAsync(
        Guid clinicId,
        AppointmentStatusWindow window,
        Guid? doctorId,
        CancellationToken cancellationToken)
    {
        var buckets = window.Buckets();
        var rowsByBucket = buckets
            .Select(b => new AppointmentStatusBucketDto
            {
                Start = DayKey(b.Start),
                EndInclusive = DayKey(b.EndInclusive)
            })
            .ToList();

        var (from, toInclusive) = window.UtcRange;
        var slots = await _appointmentRepository.GetStatusTimelineAsync(
            clinicId, from, toInclusive, doctorId, cancellationToken);

        var confirmedUpcoming = 0;

        foreach (var slot in slots)
        {
            // UTC → clinic-local FIRST. Tunisia is UTC+1, so a 00:30 booking is stored as 23:30 the previous day;
            // bucketing the raw instant would file it under yesterday, and on the 1st of a month under last month.
            var localDay = ClinicClock.ToClinicLocal(slot.StartUtc).Date;
            var index = window.IndexOf(localDay);
            if (index < 0 || index >= rowsByBucket.Count)
            {
                // Only reachable if the range filter and the bucket arithmetic ever disagreed. Skipping is right:
                // a row counted into the wrong column is a wrong chart, where a dropped row is a total that does
                // not match the sum — and the sum is what the table view shows, so it would be visible.
                continue;
            }

            var bucket = rowsByBucket[index];
            switch (AppointmentStatusClasses.Of(slot.Status))
            {
                case AppointmentStatusClass.Done:
                    bucket.Done++;
                    break;
                case AppointmentStatusClass.Upcoming:
                    bucket.Upcoming++;
                    if (slot.Status == AppointmentStatus.Confirmed)
                    {
                        confirmedUpcoming++;
                    }
                    break;
                case AppointmentStatusClass.ToClose:
                    bucket.ToClose++;
                    break;
                case AppointmentStatusClass.Cancelled:
                    bucket.Cancelled++;
                    break;
                case AppointmentStatusClass.Absent:
                    bucket.Absent++;
                    break;
            }

            bucket.Total++;
        }

        var (previousFrom, previousToInclusive) = window.PreviousUtcRange;
        var previousCounts = await _appointmentRepository.CountByStatusBetweenAsync(
            clinicId, previousFrom, previousToInclusive, cancellationToken);

        return new AppointmentStatusMixDto
        {
            From = DayKey(window.FromLocalDate),
            ToInclusive = DayKey(window.ToLocalDate),
            Granularity = window.Granularity.ToString(),
            Buckets = rowsByBucket,
            Total = rowsByBucket.Sum(b => b.Total),
            PreviousTotal = previousCounts.Values.Sum(),
            ConfirmedUpcoming = confirmedUpcoming
        };
    }

    /// <summary>
    /// A clinic-local calendar day as <c>yyyy-MM-dd</c> — locale-free and sortable, formatted by the client.
    /// <c>"o"</c>/<c>ToString()</c> would emit a time and invite the browser to build a <c>Date</c> from it, which
    /// is the timezone round-trip these keys exist to avoid.
    /// </summary>
    private static string DayKey(DateTime clinicLocalDate) => clinicLocalDate.ToString("yyyy-MM-dd");
}
