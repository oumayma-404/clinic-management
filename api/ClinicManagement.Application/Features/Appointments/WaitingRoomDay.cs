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

    /// <summary>Round 2, C9: « Annuler » after the card moved again — a colleague, a payment, a fiche.</summary>
    public const string MovedSinceCode = "waiting-room-moved-since";
    public static string MovedSince(string name) => $"{name} a déjà été déplacé·e depuis : l'action n'a pas été annulée.";

    /// <summary>Round 2, Q3: « 2 fauteuils », and both are taken.</summary>
    public const string ChairsFullCode = "waiting-room-chairs-full";
    public const string ChairsFull = "Les deux fauteuils sont occupés : terminez une séance d'abord.";

    /// <summary>Round 2, E1: « Pas là » on a patient who is no longer in the chair.</summary>
    public const string NotInChairCode = "waiting-room-not-in-chair";
    public const string NotInChair = "Ce patient n'est plus au fauteuil : la liste a été mise à jour.";

    /// <summary>Round 2, E2: « Passer en premier » on a patient no longer waiting.</summary>
    public const string NotWaitingCode = "waiting-room-not-waiting";
    public const string NotWaiting = "Ce patient n'est plus en salle d'attente : la liste a été mise à jour.";

    /// <summary>Round 2, C6: « Le recevoir aujourd'hui » on a RDV that is not on another day any more.</summary>
    public const string NotOtherDayCode = "waiting-room-not-other-day";
    public const string NotOtherDay = "Ce rendez-vous n'est plus à venir un autre jour : la liste a été mise à jour.";

    /// <summary>Round 2, Q3: « 2 fauteuils » set by somebody who is neither that dentist nor an administrator.</summary>
    public const string NotYourChairsCode = "waiting-room-not-your-chairs";
    public const string NotYourChairs = "Seuls ce praticien et un administrateur choisissent ses fauteuils.";

    /// <summary>The codes a screen should treat as « somebody else moved first » — answered 409.</summary>
    public static bool IsConflict(string? code) =>
        code is AlreadyCalledCode or AlreadyHereCode or MovedSinceCode or ChairsFullCode or NotInChairCode
            or NotWaitingCode or NotOtherDayCode;
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
    private WaitingRoomDay(
        Guid clinicId, DateTime nowUtc, IReadOnlyList<Appointment> visits, IReadOnlyList<Doctor> dentists,
        IReadOnlyDictionary<Guid, string> doctorNames, IReadOnlySet<Guid> onLeave)
    {
        ClinicId = clinicId;
        NowUtc = nowUtc;
        Visits = visits;
        Dentists = dentists;
        DoctorNames = doctorNames;
        OnLeaveToday = onLeave;
        SoleDentistId = dentists.Count == 1 ? dentists[0].Id : null;
    }

    public Guid ClinicId { get; }
    public DateTime NowUtc { get; }

    /// <summary>Every visit of the day on the board (lane other than <c>None</c>).</summary>
    public IReadOnlyList<Appointment> Visits { get; }

    public IReadOnlyList<Doctor> Dentists { get; }

    /// <summary>Every practitioner the cabinet has had, retired ones included — a card still names its dentist.</summary>
    public IReadOnlyDictionary<Guid, string> DoctorNames { get; }

    /// <summary>The active dentists on congé today — empty unless the read asked (<c>leaves</c> passed).</summary>
    public IReadOnlySet<Guid> OnLeaveToday { get; }

    public Guid? SoleDentistId { get; }
    public bool IsGroupPractice => Dentists.Count > 1;

    /// <param name="leaves">
    /// Only the board read passes it: « Dr X absent aujourd'hui » (round 2, C10) is something to show, never a
    /// rule a move obeys — reception may still seat a patient with whoever is actually there.
    /// </param>
    public static async Task<WaitingRoomDay> LoadAsync(
        Guid clinicId, DateTime nowUtc, IAppointmentRepository appointments, IDoctorRepository doctors,
        CancellationToken cancellationToken, IDoctorLeaveRepository? leaves = null)
    {
        var (from, to) = ClinicClock.TodayRangeUtc(nowUtc);
        var visits = (await appointments.GetByClinicIdAsync(clinicId, from, to, cancellationToken: cancellationToken))
            .Where(a => a.WaitingRoomLane != WaitingRoomLane.None)
            .ToList();
        var everyone = (await doctors.GetByClinicIdAsync(clinicId, cancellationToken))
            .Where(d => d.ClinicId == clinicId)
            .ToList();
        var dentists = everyone
            .Where(d => d.IsActive)
            .OrderBy(d => d.FullName)
            .ToList();
        var names = everyone.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First().FullName);

        var onLeave = new HashSet<Guid>();
        if (leaves is not null)
        {
            var today = DateOnly.FromDateTime(ClinicClock.ClinicToday(nowUtc));
            foreach (var leave in await leaves.GetOverlappingAsync(clinicId, today, today, cancellationToken))
            {
                if (leave.Covers(today, today))
                {
                    onLeave.Add(leave.DoctorId);
                }
            }
        }

        return new WaitingRoomDay(clinicId, nowUtc, visits, dentists, names, onLeave);
    }

    /// <summary>
    /// Why this visit's dentist cannot see him today (round 2, C10) — <c>OnLeave</c>, <c>NotInTeam</c>, or null.
    /// A visit naming no dentist has nobody to be absent.
    /// </summary>
    public string? DoctorUnavailable(Appointment appointment) => appointment.DoctorId switch
    {
        null => null,
        { } id when !Dentists.Any(d => d.Id == id) => "NotInTeam",
        { } id when OnLeaveToday.Contains(id) => "OnLeave",
        _ => null,
    };

    /// <summary>
    /// The patient's OTHER visit of the day that is waiting or in the chair (round 2, C4): one live card per patient,
    /// so a second « Arrivé » for the 16:00 while he waits for the 10:00 is refused by name.
    /// </summary>
    public Appointment? OtherLiveVisitOf(Appointment appointment) => Visits.FirstOrDefault(a =>
        a.Id != appointment.Id
        && a.PatientId == appointment.PatientId
        && a.WaitingRoomLane is WaitingRoomLane.Waiting or WaitingRoomLane.InChair);

    /// <summary>Does this chair belong to a dentist working « 2 fauteuils »?</summary>
    public bool HasTwoChairs(Guid? chair) => chair is { } id && Dentists.Any(d => d.Id == id && d.WorksTwoChairs);

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

    public static WaitingRoomSnapshotDto Snapshot(Appointment appointment)
    {
        var state = appointment.CaptureWaitingRoomState();
        return new WaitingRoomSnapshotDto
        {
            AppointmentId = appointment.Id,
            Lane = state.Lane.ToString(),
            ArrivedAtUtc = state.ArrivedAtUtc,
            CalledInAtUtc = state.CalledInAtUtc,
            LeftChairAtUtc = state.LeftChairAtUtc,
            LeftUnseenAtUtc = state.LeftUnseenAtUtc,
            UrgentAtUtc = state.UrgentAtUtc,
            CallSkippedAtUtc = state.CallSkippedAtUtc,
            DoctorId = state.DoctorId,
            AppointmentDateTime = state.AppointmentDateTime,
            DurationMinutes = (int)Math.Round(state.Duration.TotalMinutes),
            PlannedStartUtc = state.PlannedStartUtc,
            PlannedDurationMinutes = state.PlannedDuration is { } planned ? (int)Math.Round(planned.TotalMinutes) : null,
            BookedWithOverlap = state.BookedWithOverlap,
        };
    }

    /// <summary>The snapshot back as the domain's state, or null when its lane is not a board lane.</summary>
    public static WaitingRoomState? ToState(WaitingRoomSnapshotDto snapshot)
    {
        if (!TryParseLane(snapshot.Lane, out var lane))
        {
            return null;
        }

        return new WaitingRoomState(
            lane,
            AsUtc(snapshot.ArrivedAtUtc),
            AsUtc(snapshot.CalledInAtUtc),
            AsUtc(snapshot.LeftChairAtUtc),
            AsUtc(snapshot.LeftUnseenAtUtc),
            AsUtc(snapshot.UrgentAtUtc),
            AsUtc(snapshot.CallSkippedAtUtc),
            snapshot.DoctorId,
            AsUtc(snapshot.AppointmentDateTime)!.Value,
            TimeSpan.FromMinutes(snapshot.DurationMinutes),
            AsUtc(snapshot.PlannedStartUtc),
            snapshot.PlannedDurationMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null,
            snapshot.BookedWithOverlap);
    }

    /// <summary>
    /// The board's undo compares instants that came back through JSON: an offset-less value is UTC (every one the API
    /// sends ends in Z), a local one is converted.
    /// </summary>
    private static DateTime? AsUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } utc => utc,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        { } unspecified => DateTime.SpecifyKind(unspecified, DateTimeKind.Utc),
    };

    public static bool TryParseLane(string? value, out WaitingRoomLane lane) =>
        Enum.TryParse(value, ignoreCase: true, out lane)
        && lane != WaitingRoomLane.None
        && Enum.IsDefined(lane);

    public static string NameOf(Appointment appointment) => appointment.Patient?.GetFullName() ?? "Le patient";

    /// <summary>
    /// Every visit a change touched, as it is now — the change's <c>After</c> (round 2, C9). Read once all the moves
    /// are made, so a visit touched twice reports its final state.
    /// </summary>
    public static void RecordAfter(WaitingRoomChangeDto change, IEnumerable<Appointment> touched)
    {
        change.After.Clear();
        foreach (var appointment in touched.DistinctBy(a => a.Id))
        {
            change.After.Add(Snapshot(appointment));
        }
    }
}
