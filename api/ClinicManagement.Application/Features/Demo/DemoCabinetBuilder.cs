using System.Reflection;
using System.Text.Json;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Application.Features.DoctorLeaves.Commands;
using ClinicManagement.Application.Features.Doctors.Commands;
using ClinicManagement.Application.Features.Doctors.Queries;
using ClinicManagement.Application.Features.Notifications.Commands;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.ProcedureTypes.Queries;
using ClinicManagement.Application.Features.Users.Commands;
using ClinicManagement.Application.Features.WaitingList.Commands;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Demo;

/// <summary>
/// Writes the public demo's cabinet (spec FR-6) through the application's own commands, each sent as the team member
/// who would have done it, so numbering, money, the chart and the journal read like a real cabinet's. Every business
/// date is derived from <c>clinicToday</c>, never from the clock, so a build on any day of the year is coherent.
/// </summary>
public sealed partial class DemoCabinetBuilder
{
    private const int DaysEachSide = 14;

    private readonly IDemoSeedContext _seed;
    private readonly DateTime _today;
    private readonly string _stamp;

    private DemoMember _monica = null!;
    private DemoMember _chandler = null!;
    private DemoMember _rachel = null!;
    private Guid _monicaDoctorId;
    private Guid _chandlerDoctorId;
    private IReadOnlyList<ProcedureTypeDto> _catalogue = Array.Empty<ProcedureTypeDto>();
    private readonly List<(DemoPatientSpec Spec, Guid Id)> _patients = new();
    private readonly List<(Guid InvoiceId, decimal Amount, DateTime Day, bool Cheque)> _paidNotes = new();

    /// <param name="clinicToday">The build's day in Tunisia (date part only).</param>
    /// <param name="stamp">Keeps the members' addresses unique beside yesterday's cabinet.</param>
    public DemoCabinetBuilder(IDemoSeedContext seed, DateTime clinicToday, string stamp)
    {
        _seed = seed;
        _today = clinicToday.Date;
        _stamp = stamp;
    }

    /// <summary>The days the agenda covers: <see cref="DaysEachSide"/> working days either side of today.</summary>
    public IReadOnlyList<DateTime> AgendaDays()
    {
        var before = new List<DateTime>();
        for (var day = _today.AddDays(-1); before.Count < DaysEachSide; day = day.AddDays(-1))
        {
            if (DemoScenario.IsWorkingDay(day)) before.Add(day);
        }

        var after = new List<DateTime>();
        for (var day = _today.AddDays(1); after.Count < DaysEachSide; day = day.AddDays(1))
        {
            if (DemoScenario.IsWorkingDay(day)) after.Add(day);
        }

        before.Reverse();
        var days = new List<DateTime>(before);
        if (DemoScenario.IsWorkingDay(_today)) days.Add(_today);
        days.AddRange(after);
        return days;
    }

    public async Task BuildAsync(DemoMember dentistAdmin, CancellationToken cancellationToken)
    {
        _monica = dentistAdmin;
        await BuildTeamAsync(cancellationToken);
        await BuildSettingsAsync(cancellationToken);
        await ReadCatalogueAsync(cancellationToken);
        await BuildPatientsAsync(cancellationToken);
        await BuildAgendaAsync(cancellationToken);
        await BuildTreatmentsAsync(cancellationToken);
        await BuildMoneyAsync(cancellationToken);
        await BuildRecordsAsync(cancellationToken);
        await BalanceNetAsync(cancellationToken);
        await ClearTheBellAsync(cancellationToken);
    }

    // Every booking above raised « rendez-vous créé »; a real team would have read them long ago.
    private async Task ClearTheBellAsync(CancellationToken ct)
    {
        foreach (var member in new[] { _monica, _chandler, _rachel })
        {
            MustOk(await _seed.SendAsAsync(member, new MarkAllNotificationsReadCommand(), ct), "vider la cloche");
        }
    }

    // ---- team & settings ---------------------------------------------------------------------------------------

    private async Task BuildTeamAsync(CancellationToken ct)
    {
        var chandler = Must(await _seed.SendAsAsync(_monica, new CreateClinicUserCommand
        {
            Email = DemoScenario.EmailFor("chandler.bing", _stamp),
            FullName = DemoScenario.SecondDentistFullName,
            Role = User.RoleDoctor,
            DoctorInfo = new DoctorPersonalInfoDto
            {
                FirstName = DemoScenario.SecondDentistFirstName,
                LastName = DemoScenario.SecondDentistLastName,
                Specialty = DemoScenario.DentistSpecialty,
            },
        }, ct), "créer Dr Bing");
        _chandler = new DemoMember(chandler.UserId, chandler.Email ?? string.Empty, User.RoleDoctor);

        var rachel = Must(await _seed.SendAsAsync(_monica, new CreateClinicUserCommand
        {
            Email = DemoScenario.EmailFor("rachel.green", _stamp),
            FullName = DemoScenario.SecretaryFullName,
            Role = User.RoleSecretary,
        }, ct), "créer Rachel Green");
        _rachel = new DemoMember(rachel.UserId, rachel.Email ?? string.Empty, User.RoleSecretary);

        _monicaDoctorId = Must(await _seed.SendAsAsync(_monica, new GetMyDoctorProfileQuery(), ct), "lire Dr Geller").Id;
        _chandlerDoctorId = Must(await _seed.SendAsAsync(_chandler, new GetMyDoctorProfileQuery(), ct), "lire Dr Bing").Id;
    }

    private async Task BuildSettingsAsync(CancellationToken ct)
    {
        var hours = WorkingHoursSerializer.Weekdays.Select(day =>
        {
            // A tuple has no null: FirstOrDefault on a closed day hands back Sunday at 00:00, hence Any first.
            var isOpen = DemoScenario.Hours.Any(h => h.Day.ToString() == day);
            var open = DemoScenario.Hours.FirstOrDefault(h => h.Day.ToString() == day);
            return isOpen
                ? new WorkingDayDto
                {
                    Day = day,
                    Enabled = true,
                    From = Hhmm(open.From),
                    To = Hhmm(open.To),
                    BreakFrom = open.BreakFrom is { } bf ? Hhmm(bf) : null,
                    BreakTo = open.BreakTo is { } bt ? Hhmm(bt) : null,
                }
                : new WorkingDayDto { Day = day, Enabled = false, From = "09:00", To = "18:00" };
        }).ToList();

        Must(await _seed.SendAsAsync(_monica, new UpdateClinicCommand
        {
            Name = DemoScenario.CabinetName,
            WorkingHoursJson = JsonSerializer.Serialize(hours, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        }, ct), "enregistrer les horaires");

        await using var header = Asset("entete.png");
        await using var footer = Asset("pied.png");
        Must(await _seed.SendAsAsync(_monica, new UpdateClinicLetterheadCommand
        {
            Header = header, HeaderFileName = "entete.png", HeaderLength = header.Length,
            Footer = footer, FooterFileName = "pied.png", FooterLength = footer.Length,
        }, ct), "enregistrer l'en-tête");

        await using var cachet = Asset("cachet.png");
        Must(await _seed.SendAsAsync(_monica, new UpdateDoctorProfileCommand
        {
            DoctorId = _monicaDoctorId,
            CachetStream = cachet, CachetFileName = "cachet.png", CachetLength = cachet.Length,
        }, ct), "enregistrer le cachet");
    }

    private async Task ReadCatalogueAsync(CancellationToken ct)
    {
        var page = Must(await _seed.SendAsAsync(_monica, new GetProcedureTypesQuery(), ct), "lire les actes");
        _catalogue = page.Items.Where(p => p.IsActive).ToList();
    }

    // ---- patients ----------------------------------------------------------------------------------------------

    private async Task BuildPatientsAsync(CancellationToken ct)
    {
        var index = 0;
        foreach (var spec in DemoScenarioPatients.All)
        {
            // Spread birthdays over the year so ages read as people, not as a list.
            var birth = _today.AddYears(-spec.AgeYears).AddDays(-(index * 37 % 300) - 1);
            var patient = Must(await _seed.SendAsAsync(_rachel, DemoScenarioPatients.CommandFor(spec, index, birth, _today), ct),
                $"créer {spec.FullName}");
            _patients.Add((spec, patient.Id));
            _teethTaken[patient.Id] = ReservedTeeth.TryGetValue(spec.FullName, out var reserved) ? new HashSet<int>(reserved) : new HashSet<int>();
            index++;
        }

        // « À traiter » marks a dentist charted at an examination, so the odontograms are not blank.
        foreach (var (name, tooth, condition) in Diagnoses)
        {
            var patient = PatientNamed(name);
            Must(await _seed.SendAsAsync(_monica, new DiagnoseToothCommand
            {
                PatientId = patient.Id,
                ToothNumber = tooth,
                Condition = condition,
                Surfaces = condition == "Carie" ? "O" : null,
            }, ct), $"charter la dent {tooth} de {name}");
        }
    }

    // Teeth a diagnosis or a treatment below already speaks for, so an agenda fiche never charts over them.
    private static readonly Dictionary<string, int[]> ReservedTeeth = new()
    {
        ["Ross Geller"] = new[] { 36, 37 }, ["Mike Hannigan"] = new[] { 24 }, ["Gunther Centralperk"] = new[] { 11 },
        ["Frank Buffay"] = new[] { 38 }, ["Jack Geller"] = new[] { 46, 47 }, ["Leonard Green"] = new[] { 14, 15, 16 },
        ["Pete Becker"] = new[] { 36 }, ["Paul Stevens"] = new[] { 26 }, ["Eddie Menuek"] = new[] { 45 },
        ["Jill Green"] = new[] { 11, 21 },
    };

    private static readonly (string Patient, int Tooth, string Condition)[] Diagnoses =
    {
        ("Ross Geller", 36, "Carie"), ("Ross Geller", 37, "Carie"), ("Mike Hannigan", 24, "Carie"),
        ("Gunther Centralperk", 11, "Fracture"), ("Frank Buffay", 38, "DentIncluse"),
        ("Jack Geller", 46, "ExtraitAbsent"), ("Jack Geller", 47, "ExtraitAbsent"), ("Leonard Green", 15, "ExtraitAbsent"),
        ("Pete Becker", 36, "ExtraitAbsent"), ("Paul Stevens", 26, "LesionPeriapicale"), ("Eddie Menuek", 45, "Carie"),
    };

    // ---- agenda & fiches ---------------------------------------------------------------------------------------

    // Two half-day shapes, alternated day by day: every half-day is booked edge to edge, nothing overlaps, the
    // lunch break stays empty, and each half-day ends on a 30-minute slot at 17:30 (where Ben's series sits).
    private static readonly (TimeSpan Start, int Minutes)[][] Mornings =
    {
        new[] { (H(9, 0), 30), (H(9, 30), 30), (H(10, 0), 60), (H(11, 0), 30), (H(11, 30), 30), (H(12, 0), 60) },
        new[] { (H(9, 0), 60), (H(10, 0), 30), (H(10, 30), 30), (H(11, 0), 60), (H(12, 0), 30), (H(12, 30), 30) },
    };

    private static readonly (TimeSpan Start, int Minutes)[][] Afternoons =
    {
        new[] { (H(14, 0), 60), (H(15, 0), 30), (H(15, 30), 30), (H(16, 0), 60), (H(17, 0), 30), (H(17, 30), 30) },
        new[] { (H(14, 0), 30), (H(14, 30), 30), (H(15, 0), 60), (H(16, 0), 30), (H(16, 30), 30), (H(17, 0), 30), (H(17, 30), 30) },
    };

    private static readonly TimeSpan SeriesStart = H(17, 30);

    // Acts without a protocol, so a past visit's fiche records them as finished. Each list cycles through as many
    // catalogue colours as it can, so the agenda shows the palette rather than three hues.
    private static readonly string[] ShortActs =
    {
        "Consultation / examen bucco-dentaire", "Détartrage", "Soin de carie / obturation", "Radiographie panoramique",
        "Extraction simple", "Séance orthodontique (contrôle / activation)", "Coiffage pulpaire", "Contrôle / suivi",
        "Couronne provisoire", "Radiographie rétro-alvéolaire", "Extraction de racine (alvéolectomie)",
    };

    private static readonly string[] LongActs =
    {
        "Traitement de canal (dévitalisation)", "Soin de carie / obturation", "Détartrage",
        "Consultation / examen bucco-dentaire",
    };

    // Future hours may book the first séance of a longer treatment, which adds the colours only those acts carry.
    private static readonly string[] LongActsAhead =
    {
        "Blanchiment dentaire", "Implant dentaire", "Couronne / bridge (par élément)", "Traitement de canal (dévitalisation)",
        "Traitement orthodontique (multi-attaches)", "Prothèse amovible (partielle / complète)", "Facette (par élément)",
        "Gouttière occlusale (bruxisme)", "Greffe osseuse / comblement", "Retraitement endodontique",
        "Traitement parodontal (surfaçage / curetage)",
    };

    private static readonly string[] ChildActs =
    {
        "Soin dentaire enfant (dent de lait)", "Scellement de sillons", "Application de fluor (par arcade)",
        "Couronne pédodontique préformée", "Consultation / examen bucco-dentaire",
    };

    private static readonly string[] MixedDentitionActs =
    {
        "Séance orthodontique (contrôle / activation)", "Scellement de sillons", "Soin dentaire enfant (dent de lait)",
        "Application de fluor (par arcade)",
    };

    private readonly Dictionary<Guid, HashSet<int>> _teethTaken = new();

    /// <summary>The day Dr Bing is on congé: four working days ahead, so the agenda shows it this week or next.</summary>
    public DateTime LeaveDay() => AgendaDays().Where(d => d > _today).Skip(3).First();

    /// <summary>The first Wednesday after today: Ben Geller's weekly ortho series starts there, at 17:30.</summary>
    public DateTime FirstSeriesDay()
    {
        var day = _today.AddDays(1);
        while (day.DayOfWeek != DayOfWeek.Wednesday) day = day.AddDays(1);
        return day;
    }

    /// <summary>
    /// One day's book: each half-day belongs to one dentist (they alternate, so the agenda reads as one chair shared
    /// in turn), Monica takes Saturday and Dr Bing's congé, and the slot Ben's series will take is left free.
    /// </summary>
    public IReadOnlyList<(TimeSpan Start, int Minutes, bool Monica)> SlotsFor(DateTime day, int dayIndex)
    {
        var shape = dayIndex % 2;
        var monicaMorning = dayIndex % 2 == 0 || day.DayOfWeek == DayOfWeek.Saturday || day == LeaveDay();
        var monicaAfternoon = dayIndex % 2 == 1 || day == LeaveDay();
        var seriesDays = Enumerable.Range(0, 4).Select(w => FirstSeriesDay().AddDays(7 * w)).ToHashSet();

        var slots = Mornings[shape].Select(s => (s.Start, s.Minutes, monicaMorning)).ToList();
        if (day.DayOfWeek != DayOfWeek.Saturday)
        {
            slots.AddRange(Afternoons[shape]
                .Where(s => !(seriesDays.Contains(day) && s.Start == SeriesStart))
                .Select(s => (s.Start, s.Minutes, monicaAfternoon)));
        }

        return slots;
    }

    private async Task BuildAgendaAsync(CancellationToken ct)
    {
        var leaveDay = LeaveDay();
        Must(await _seed.SendAsAsync(_rachel, new CreateDoctorLeaveCommand
        {
            DoctorId = _chandlerDoctorId,
            StartDate = DateOnly.FromDateTime(leaveDay),
            EndDate = DateOnly.FromDateTime(leaveDay),
            Note = "Congrès",
        }, ct), "poser le congé de Dr Bing");

        var days = AgendaDays();
        var pastDays = days.Where(d => d < _today).ToList();
        var visitTurn = 0;
        var actTurn = 0;

        for (var dayIndex = 0; dayIndex < days.Count; dayIndex++)
        {
            var day = days[dayIndex];
            var slots = SlotsFor(day, dayIndex);
            for (var s = 0; s < slots.Count; s++)
            {
                var (start, minutes, isMonica) = slots[s];
                var (member, doctorId) = isMonica ? (_monica, _monicaDoctorId) : (_chandler, _chandlerDoctorId);
                // One patient after another down the list, so every patient has visits behind them by the first fortnight.
                var patient = _patients[visitTurn++ % _patients.Count];
                var act = ActForVisit(patient.Spec, minutes, past: day < _today, actTurn++);
                var at = ClinicClock.ToUtc(day + start);

                var visit = Must(await _seed.SendAsAsync(_rachel, new CreateAppointmentCommand
                {
                    PatientId = patient.Id,
                    DoctorId = doctorId,
                    AppointmentDateTime = at,
                    DurationMinutes = minutes,
                    Procedures = new List<AppointmentProcedureRequest> { new() { ProcedureTypeId = act.Id } },
                }, ct), $"réserver {patient.Spec.FirstName} le {day:dd/MM}");

                if (day < _today)
                {
                    // Two séances left for « À clôturer », one patient who did not come, the rest recorded and paid.
                    var lastOfDay = s == slots.Count - 1;
                    if (lastOfDay && (day == pastDays[^1] || (pastDays.Count >= 4 && day == pastDays[^4]))) continue;
                    if (s == 0 && pastDays.Count >= 2 && day == pastDays[^2])
                    {
                        await SetStatusAsync(visit.Id, "NoShow", ct);
                        continue;
                    }

                    await RecordFicheAsync(member, doctorId, patient, act, visit.Id, at, visitTurn, ct);
                    if (s == 1 && day == pastDays[^1])
                    {
                        MustOk(await _seed.SendAsAsync(_rachel, new UpdateAppointmentCommand { Id = visit.Id, LateArrivalMinutes = 10 }, ct),
                            "noter un retard");
                    }
                }
            }
        }

        await BuildSeriesAndWaitingListAsync(ct);
    }

    private async Task SetStatusAsync(Guid appointmentId, string status, CancellationToken ct) =>
        MustOk(await _seed.SendAsAsync(_rachel, new UpdateAppointmentCommand { Id = appointmentId, Status = status }, ct),
            $"passer un rendez-vous à « {status} »");

    private ProcedureTypeDto ActForVisit(DemoPatientSpec patient, int minutes, bool past, int turn)
    {
        if (patient.IsChild)
        {
            var kids = patient.Dentition == "Mixed" ? MixedDentitionActs : ChildActs;
            return ActFor(kids[turn % kids.Length]);
        }

        if (minutes <= 30) return ActFor(ShortActs[turn % ShortActs.Length]);
        if (past) return ActFor(LongActs[turn % LongActs.Length]);
        var name = LongActsAhead[turn % LongActsAhead.Length];
        return _catalogue.FirstOrDefault(p => p.Name == name) ?? ActFor(name);
    }

    private async Task RecordFicheAsync(
        DemoMember dentist, Guid doctorId, (DemoPatientSpec Spec, Guid Id) patient, ProcedureTypeDto act,
        Guid appointmentId, DateTime at, int turn, CancellationToken ct)
    {
        var teeth = TeethFor(act, patient, turn);
        if (teeth is null)
        {
            // Every tooth this act could treat is already spoken for: the séance becomes an examination.
            act = ActFor("Consultation / examen bucco-dentaire");
            teeth = new List<int>();
        }

        var cost = act.DefaultCost ?? 0m;
        var cheque = turn % 9 == 0;
        var local = ClinicClock.ToClinicLocal(at);
        var record = Must(await _seed.SendAsAsync(dentist, new CreateDentalRecordCommand
        {
            PatientId = patient.Id,
            InterventionDate = at,
            AppointmentId = appointmentId,
            DoctorId = doctorId,
            IsAdultTeeth = teeth.Count == 0 ? !patient.Spec.IsChild : teeth.All(t => t % 10 <= 8 && t / 10 <= 4),
            Acts = new List<DentalActInput>
            {
                new()
                {
                    ProcedureTypeId = act.Id,
                    ProcedureName = act.Name,
                    Cost = cost,
                    UnitCost = cost,
                    IsPerTooth = false,
                    ToothNumbers = teeth,
                    ResultingCondition = act.ResultingCondition,
                },
            },
            Notes = FicheNote(act) is { } note ? new List<string> { note } : new List<string>(),
            AmountPaid = cost,
            PaymentMethod = cheque ? "Cheque" : turn % 4 == 0 ? "Card" : "Cash",
            ChequeNumber = cheque ? $"{4512000 + turn}" : null,
            ChequeBankName = cheque ? ChequeBanks[turn % ChequeBanks.Length] : null,
            ChequeDueDate = cheque ? DateTime.SpecifyKind(local.Date, DateTimeKind.Utc) : null,
        }, ct), $"enregistrer la fiche de {patient.Spec.FirstName} du {local:dd/MM}");
        if (record.Billing?.InvoiceId is { } invoiceId)
        {
            _paidNotes.Add((invoiceId, cost, local.Date, cheque));
        }
    }

    private async Task BuildSeriesAndWaitingListAsync(CancellationToken ct)
    {
        var firstWednesday = FirstSeriesDay();
        var ortho = _catalogue.FirstOrDefault(p => p.Name.StartsWith("Séance orthodontique", StringComparison.Ordinal));
        Must(await _seed.SendAsAsync(_rachel, new CreateRecurringSeriesCommand
        {
            PatientId = PatientNamed("Ben Geller").Id,
            DoctorId = _monicaDoctorId,
            StartDateTime = ClinicClock.ToUtc(firstWednesday + SeriesStart),
            DurationMinutes = 30,
            Frequency = "Weekly",
            Interval = 1,
            OccurrenceCount = 4,
            ProcedureTypeId = ortho?.Id,
            Notes = "Contrôle de l'appareil",
        }, ct), "créer la série de Ben Geller");

        Must(await _seed.SendAsAsync(_rachel, new CreateWaitingListEntryCommand
        {
            PatientId = PatientNamed("Pete Becker").Id,
            PreferredDoctorId = _monicaDoctorId,
            Priority = "High",
            DesiredTimeframe = "Dès que possible",
            Note = "Préfère le matin — bilan implantaire",
        }, ct), "inscrire Pete Becker en liste d'attente");
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private ProcedureTypeDto ActFor(string name) =>
        _catalogue.FirstOrDefault(p => p.Name == name && p.DefaultSteps.Count == 0)
        ?? _catalogue.First(p => p.DefaultSteps.Count == 0);

    /// <summary>
    /// The tooth this act treats today, never one a diagnosis, a treatment or an earlier fiche already speaks for, so
    /// no tooth is filled after it was extracted. Null when every candidate is taken.
    /// </summary>
    private List<int>? TeethFor(ProcedureTypeDto act, (DemoPatientSpec Spec, Guid Id) patient, int turn)
    {
        var child = patient.Spec.Dentition == "Child";
        int[] pool = act.Name switch
        {
            "Soin de carie / obturation" => new[] { 16, 26, 36, 46, 15, 25, 35, 45, 14, 24, 17, 27, 37, 47 },
            "Extraction simple" or "Extraction de racine (alvéolectomie)" => new[] { 18, 28, 38, 48 },
            "Traitement de canal (dévitalisation)" => new[] { 26, 36, 16, 46, 25, 35 },
            "Coiffage pulpaire" => new[] { 15, 25, 35, 45, 14, 24 },
            "Couronne provisoire" => new[] { 12, 22, 13, 23 },
            "Radiographie rétro-alvéolaire" => new[] { 36, 46, 16, 26 },
            "Soin dentaire enfant (dent de lait)" => new[] { 54, 64, 74, 84, 55, 65, 75, 85 },
            "Couronne pédodontique préformée" => new[] { 74, 84, 75, 85 },
            "Scellement de sillons" => child ? new[] { 55, 65, 75, 85 } : new[] { 16, 26, 36, 46 },
            _ => Array.Empty<int>(),
        };
        if (pool.Length == 0) return new List<int>();

        var taken = _teethTaken[patient.Id];
        for (var k = 0; k < pool.Length; k++)
        {
            var tooth = pool[(turn + k) % pool.Length];
            if (taken.Contains(tooth)) continue;
            // Only an act that leaves a state on the tooth takes it; an X-ray or a provisional crown does not.
            if (act.ResultingCondition is { } state && state != "Sain") taken.Add(tooth);
            return new List<int> { tooth };
        }

        return null;
    }

    private static string? FicheNote(ProcedureTypeDto act) => act.Name switch
    {
        "Détartrage" => "Saignement gingival léger — brossage expliqué",
        "Extraction simple" => "Consignes post-opératoires remises",
        "Traitement de canal (dévitalisation)" => "Couronne à prévoir",
        _ => null,
    };

    private (DemoPatientSpec Spec, Guid Id) PatientNamed(string fullName) =>
        _patients.First(p => $"{p.Spec.FirstName} {p.Spec.LastName}" == fullName);

    private static Stream Asset(string fileName)
    {
        var resource = $"{typeof(DemoCabinetBuilder).Namespace}.Assets.{fileName}";
        var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Demo: asset {resource} is not embedded.");
        var copy = new MemoryStream();
        embedded.CopyTo(copy);
        embedded.Dispose();
        copy.Position = 0;
        return copy;
    }

    private static TimeSpan H(int hours, int minutes) => new(hours, minutes, 0);

    private static string Hhmm(TimeSpan time) => $"{time.Hours:00}:{time.Minutes:00}";

    private static void MustOk(Result result, string what)
    {
        if (result.IsFailure) throw new InvalidOperationException($"Démo : échec — {what} : {result.Error}");
    }

    private static T Must<T>(Result<T> result, string what) =>
        result.IsSuccess && result.Value is not null
            ? result.Value
            : throw new InvalidOperationException($"Démo : échec — {what} : {result.Error}");
}
