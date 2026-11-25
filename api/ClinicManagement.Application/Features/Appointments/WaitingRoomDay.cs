using ClinicManagement.Application.Common;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Appointments;

/// <summary>
/// The salle d'attente's refusals: each French sentence beside its code, <c>SubscriptionRefusals</c>' shape — a
/// sentence reworded freely must never change what a caller does, so callers branch on the code.
/// </summary>
public static class WaitingRoomRefusals
{
    public const string NotFoundCode = "waiting-room-not-found";
    public const string NotFound = "Ce rendez-vous est introuvable.";

    public const string NotOnBoardCode = "waiting-room-not-on-board";
    public const string NotOnBoard = "Ce rendez-vous n'a pas sa place dans la salle d'attente (sans patient, annulé ou absent).";

    public const string NotTodayCode = "waiting-room-not-today";
    public const string NotToday = "La salle d'attente ne concerne que les rendez-vous d'aujourd'hui.";

    public const string UnknownLaneCode = "waiting-room-unknown-lane";
    public const string UnknownLane = "Destination inconnue.";

    public const string WalkInHasNoSlotCode = "waiting-room-walk-in-has-no-slot";

    public const string AlreadyHereCode = "waiting-room-already-here";
    public static string AlreadyHere(string name) => $"{name} est déjà dans la salle d'attente.";

    public const string NeedsDentistCode = "waiting-room-needs-dentist";
    public const string NeedsDentist = "Choisissez le praticien qui recevra ce patient.";

    public const string UnknownDentistCode = "waiting-room-unknown-dentist";
    public const string UnknownDentist = "Ce praticien n'existe pas ou ne travaille plus au cabinet.";

    public const string NotADentistCode = "waiting-room-not-a-dentist";
    public const string NotADentist = "Seul un praticien appelle le patient suivant : votre compte n'est lié à aucun praticien.";

    public const string AlreadyCalledCode = "waiting-room-already-called";
    public const string AlreadyCalled = "Ce patient vient d'être appelé : la liste a été mise à jour.";

    public const string OtherDentistCode = "waiting-room-other-dentist";
    public const string OtherDentist = "Ce patient attend un autre praticien.";

    public const string NobodyCode = "waiting-room-nobody";
    public const string Nobody = "Personne n'attend et le fauteuil est libre.";

    public const string IncoherentCode = "waiting-room-incoherent";

    public const string WalkInHasWorkCode = "waiting-room-walk-in-has-work";
    public const string WalkInHasWork = "Ce patient a déjà été reçu : il ne peut plus être retiré de la journée.";

    /// <summary>The codes a screen should treat as « somebody else moved first » — answered 409.</summary>
    public static bool IsConflict(string? code) => code is AlreadyCalledCode or AlreadyHereCode;
}

/// <summary>
/// Today's board for one cabinet: its visits and its dentists, loaded once per request so every command and the
/// read agree on what « today », « the chair » and « the queue » mean.
///
/// <para><b>One chair per dentist.</b> A visit's chair is its dentist — or, when it names none, the cabinet's only
/// dentist. Agenda bookings at reception often name nobody (90 of 121 measured on prod), so without that fallback a
/// one-dentist cabinet would seat two patients at once: one « Dr X » chair and one « nobody » chair. In a cabinet de
/// groupe an unassigned visit stays unassigned until a dentist calls him, who then takes him.</para>
/// </summary>
public sealed class WaitingRoomDay
{
    private WaitingRoomDay(Guid clinicId, DateTime nowUtc, IReadOnlyList<Appointment> visits, IReadOnlyList<Doctor> dentists)
    {
        ClinicId = clinicId;
        NowUtc = nowUtc;
        Visits = visits;
        Dentists = dentists;
        SoleDentistId = dentists.Count == 1 ? dentists[0].Id : null;
    }

    public Guid ClinicId { get; }
    public DateTime NowUtc { get; }

    /// <summary>Every visit of the day on the board (lane other than <c>None</c>).</summary>
    public IReadOnlyList<Appointment> Visits { get; }

    public IReadOnlyList<Doctor> Dentists { get; }
    public Guid? SoleDentistId { get; }
    public bool IsGroupPractice => Dentists.Count > 1;

    public static async Task<WaitingRoomDay> LoadAsync(
        Guid clinicId, DateTime nowUtc, IAppointmentRepository appointments, IDoctorRepository doctors,
        CancellationToken cancellationToken)
    {
        var (from, to) = ClinicClock.TodayRangeUtc(nowUtc);
        var visits = (await appointments.GetByClinicIdAsync(clinicId, from, to, cancellationToken: cancellationToken))
            .Where(a => a.WaitingRoomLane != WaitingRoomLane.None)
            .ToList();
        var dentists = (await doctors.GetByClinicIdAsync(clinicId, cancellationToken))
            .Where(d => d.IsActive && d.ClinicId == clinicId)
            .OrderBy(d => d.FullName)
            .ToList();
        return new WaitingRoomDay(clinicId, nowUtc, visits, dentists);
    }

    public Appointment? Find(Guid appointmentId) => Visits.FirstOrDefault(a => a.Id == appointmentId);

    /// <summary>The chair a visit belongs to — see the class note.</summary>
    public Guid? ChairOf(Appointment appointment) => appointment.DoctorId ?? SoleDentistId;

    public IEnumerable<Appointment> InLane(WaitingRoomLane lane) => Visits.Where(a => a.WaitingRoomLane == lane);

    /// <summary>Who is in this chair now — normally one, never assumed to be.</summary>
    public IReadOnlyList<Appointment> InChairOf(Guid? chair) =>
        InLane(WaitingRoomLane.InChair).Where(a => ChairOf(a) == chair).ToList();

    /// <summary>
    /// The queue a dentist serves, in <see cref="WaitingRoomOrder"/>: his own patients, plus — in a cabinet de groupe
    /// — the unassigned ones, whom any dentist may take.
    /// </summary>
    public IReadOnlyList<Appointment> QueueOf(Guid dentistId) => WaitingRoomOrder.Order(
        InLane(WaitingRoomLane.Waiting).Where(a => ChairOf(a) == dentistId || (IsGroupPractice && a.DoctorId is null)));

    public static WaitingRoomSnapshotDto Snapshot(Appointment appointment) => new()
    {
        AppointmentId = appointment.Id,
        Lane = appointment.WaitingRoomLane.ToString(),
        ArrivedAtUtc = appointment.ArrivedAtUtc,
        CalledInAtUtc = appointment.CalledInAtUtc,
        LeftChairAtUtc = appointment.LeftChairAtUtc,
        DoctorId = appointment.DoctorId,
    };

    public static bool TryParseLane(string? value, out WaitingRoomLane lane) =>
        Enum.TryParse(value, ignoreCase: true, out lane)
        && lane != WaitingRoomLane.None
        && Enum.IsDefined(lane);

    public static string NameOf(Appointment appointment) => appointment.Patient?.GetFullName() ?? "Le patient";
}
