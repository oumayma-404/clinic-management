using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments;

/// <summary>
/// The practitioner a visit is booked with, resolved from <see cref="Appointment.DoctorId"/> — the read side of
/// a column nothing was reading.
///
/// <para><b>The defect this closes.</b> <c>Appointment</c> carries two unrelated fields: <c>DoctorId</c>, a real
/// FK to <see cref="Doctor"/>, and <c>DoctorName</c>, a free-text snapshot. Every appointment read mapped the
/// <i>snapshot</i>, and no write path ever populated it — <c>CreateAppointmentCommand</c> passes
/// <c>request.DoctorName</c> straight into the constructor and no client sends that key, while
/// <c>SetDoctorId</c> deliberately does not touch it. Measured on a real database: of 42 appointments, 10 had a
/// <c>DoctorId</c> and 3 had a <c>DoctorName</c>, and the two sets were <b>disjoint</b>. So every visit booked
/// through the UI rendered « — » in « Praticien » while the practitioner it names was stored all along.</para>
///
/// <para><b>Why the roster and not a batched by-id read.</b> A clinic has a handful of practitioners — the
/// bound is the roster, not the page — so one <c>GetByClinicIdAsync</c> answers for every row and needs no new
/// repository method. It is also the read <c>PractitionerRenderSnapshot</c> already tenant-checks against, so a
/// <c>DoctorId</c> belonging to another practice resolves to nothing here rather than leaking a name.</para>
///
/// <para><b>Why the live name wins over the snapshot.</b> <c>DoctorId</c> is the source of truth: it is what the
/// praticien filter narrows on and what <c>PractitionerAttribution</c> credits money and clinical work to. A
/// dentist who corrects the spelling of their own name should see it corrected everywhere, which a frozen
/// snapshot cannot do. The snapshot is kept as a <b>fallback</b> because it is the only thing rows with no
/// <c>DoctorId</c> have — a hand-typed name, or a seeded row — and dropping it would blank those.</para>
///
/// <para>A shared helper rather than inline code in each of the three reads, for
/// <see cref="AppointmentInvoiceLinks"/>' reason: « which name does this visit show » must have one answer, or
/// the agenda, the patient's file and « À clôturer » drift and each looks right on its own.</para>
/// </summary>
public static class AppointmentDoctorNames
{
    /// <summary>
    /// The clinic's practitioners by id. Read once per request; pass the result to <see cref="For"/> per row.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> ResolveRosterAsync(
        IDoctorRepository doctorRepository,
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var doctors = await doctorRepository.GetByClinicIdAsync(clinicId, cancellationToken);

        // Last one wins on a duplicate id, which cannot happen — but `ToDictionary` throws on one, and a read
        // that 500s over the practitioner column would take the whole screen down for a name.
        var roster = new Dictionary<Guid, string>();
        foreach (var doctor in doctors)
        {
            var name = doctor.FullName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                roster[doctor.Id] = name;
            }
        }

        return roster;
    }

    /// <summary>
    /// The name to show for one appointment: the practitioner's current name, else the stored snapshot, else
    /// null. Null stays null — many bookings genuinely name no practitioner, and « Praticien inconnu » would
    /// assert one exists.
    /// </summary>
    public static string? For(
        Guid? doctorId,
        string? storedName,
        IReadOnlyDictionary<Guid, string> roster)
    {
        if (doctorId is Guid id && roster.TryGetValue(id, out var live))
        {
            return live;
        }

        return string.IsNullOrWhiteSpace(storedName) ? null : storedName;
    }
}
