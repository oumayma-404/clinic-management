using System.Text.RegularExpressions;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Application.Features.Dashboard;
using ClinicManagement.Application.Features.Dashboard.Queries;
using ClinicManagement.Application.Features.Demo;
using ClinicManagement.Application.Features.DoctorLeaves.Commands;
using ClinicManagement.Application.Features.Doctors.Commands;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.ProcedureTypes.Queries;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Application.Features.Documents.Commands;
using ClinicManagement.Application.Features.Expenses.Commands;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Application.Features.LabOrders.Commands;
using ClinicManagement.Application.Features.Stock.Commands;
using ClinicManagement.Application.Features.Suppliers.Commands;
using ClinicManagement.Application.Features.Users.Commands;
using ClinicManagement.Application.Features.WaitingList.Commands;
using ClinicManagement.UnitTests.Common;
using MediatR;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Demo;

/// <summary>
/// demo-cabinet US-4 / FR-6: the scenario's shape, against a seed context that records every command. What it pins is
/// what a rebuild on an odd day could get wrong silently — a visit on a closed day, outside hours, or two visits in
/// one dentist's chair — and that every area of part A is written, by the member who would have written it.
/// </summary>
public class DemoCabinetBuilderTests
{
    private static readonly DemoMember Monica = new("monica", "monica@demo.apexa.tn", "admin");

    private sealed class RecordingSeed : IDemoSeedContext
    {
        public List<(DemoMember Member, object Request)> Sent { get; } = new();

        public Guid ClinicId { get; } = Guid.NewGuid();

        public Task<TResponse> SendAsAsync<TResponse>(DemoMember member, IRequest<TResponse> request, CancellationToken ct)
        {
            Sent.Add((member, request));
            return Task.FromResult((TResponse)Respond(request, typeof(TResponse)));
        }

        // A successful Result<T> whose value carries a fresh id, so the builder can chain on it.
        private static object Respond(object request, Type response)
        {
            if (response == typeof(Result)) return Result.Success();
            var valueType = response.GetGenericArguments()[0];
            object value = request switch
            {
                GetProcedureTypesQuery => PagedResult<ProcedureTypeDto>.Unpaged(Catalogue()),
                CreateClinicUserCommand c => new CreatedClinicUserDto { UserId = Guid.NewGuid().ToString(), Email = c.Email, Role = c.Role },
                // A devis comes back with its acts and each act's séances, as the catalogue protocol would give them.
                CreateTreatmentPlanCommand c => new TreatmentPlanDto
                {
                    Items = c.Items.Select(_ => new TreatmentPlanItemDto
                    {
                        Id = Guid.NewGuid(),
                        Steps = Enumerable.Range(1, 3).Select(n => new TreatmentPlanItemStepDto { Id = Guid.NewGuid(), SequenceNumber = n }).ToList(),
                    }).ToList(),
                },
                // A paid fiche issues its note d'honoraires.
                CreateDentalRecordCommand { AmountPaid: > 0 } => new DentalRecordDto { Billing = new DentalRecordBillingDto { InvoiceId = Guid.NewGuid() } },
                // The 30 days before the lab bill: 9 000 DT net.
                GetDashboardQuery => new DashboardDto { Money = new DashboardMoneyDto { Net = PeriodComparison.Of(9000m, 1500m) } },
                _ => Activator.CreateInstance(valueType)!,
            };
            valueType.GetProperty("Id")?.SetValue(value, Guid.NewGuid());
            return response.GetMethod("Success")!.Invoke(null, new[] { value })!;
        }
    }

    private static List<ProcedureTypeDto> Catalogue() => new[]
    {
        "Consultation / examen bucco-dentaire", "Détartrage", "Soin de carie / obturation", "Contrôle / suivi",
        "Extraction simple", "Radiographie panoramique", "Traitement de canal (dévitalisation)",
        "Soin dentaire enfant (dent de lait)", "Scellement de sillons", "Application de fluor (par arcade)",
        "Séance orthodontique (contrôle / activation)", "Couronne / bridge (par élément)", "Implant dentaire",
        "Blanchiment dentaire", "Facette (par élément)", "Couronne sur implant",
    }.Select(n => new ProcedureTypeDto { Id = Guid.NewGuid(), Name = n, DefaultCost = 50m, IsActive = true, DefaultDurationMinutes = 30 })
        .ToList();

    private static async Task<(RecordingSeed Seed, DemoCabinetBuilder Builder)> BuildOn(DateTime clinicToday)
    {
        var seed = new RecordingSeed();
        var builder = new DemoCabinetBuilder(seed, clinicToday, "20261103030000");
        await builder.BuildAsync(Monica, CancellationToken.None);
        return (seed, builder);
    }

    public static IEnumerable<object[]> Anchors() => new[]
    {
        new object[] { new DateTime(2026, 11, 3) },   // a Tuesday
        new object[] { new DateTime(2026, 11, 8) },   // a Sunday — no visit today
        new object[] { new DateTime(2027, 1, 1) },    // 1 January, a Friday
        new object[] { new DateTime(2026, 11, 7) },   // a Saturday — morning only
    };

    [Theory] // [FR-6] every visit falls on a working day, inside the hours, out of the lunch break
    [MemberData(nameof(Anchors))]
    public async Task Every_Visit_Is_Inside_The_Cabinets_Hours(DateTime clinicToday)
    {
        var (seed, _) = await BuildOn(clinicToday);
        var visits = seed.Sent.Select(s => s.Request).OfType<CreateAppointmentCommand>().ToList();

        Assert.NotEmpty(visits);
        foreach (var visit in visits)
        {
            var start = ClinicClock.ToClinicLocal(visit.AppointmentDateTime);
            var end = start.AddMinutes(visit.DurationMinutes);
            var day = DemoScenario.Hours.Single(h => h.Day == start.DayOfWeek);
            Assert.True(start.TimeOfDay >= day.From && end.TimeOfDay <= day.To, $"{start:ddd dd/MM HH:mm} outside hours");
            if (day.BreakFrom is { } bf && day.BreakTo is { } bt)
            {
                Assert.False(start.TimeOfDay < bt && end.TimeOfDay > bf, $"{start:ddd dd/MM HH:mm} overlaps the lunch break");
            }
        }
    }

    [Theory] // [AC-2.2] no two visits overlap — not even across the two dentists — and nobody is booked during Dr Bing's congé
    [MemberData(nameof(Anchors))]
    public async Task No_Visit_Overlaps_Another_And_The_Conge_Is_Empty(DateTime clinicToday)
    {
        var (seed, builder) = await BuildOn(clinicToday);
        var sent = seed.Sent.Select(s => s.Request).ToList();
        var visits = sent.OfType<CreateAppointmentCommand>().ToList();
        var series = Assert.Single(sent.OfType<CreateRecurringSeriesCommand>());
        var occupied = visits.Select(v => (Start: v.AppointmentDateTime, Minutes: v.DurationMinutes))
            .Concat(Enumerable.Range(0, series.OccurrenceCount!.Value).Select(w => (Start: series.StartDateTime.AddDays(7 * w), Minutes: series.DurationMinutes)))
            .OrderBy(v => v.Start)
            .ToList();

        for (var i = 1; i < occupied.Count; i++)
        {
            Assert.True(occupied[i].Start >= occupied[i - 1].Start.AddMinutes(occupied[i - 1].Minutes),
                $"{ClinicClock.ToClinicLocal(occupied[i].Start):ddd dd/MM HH:mm} overlaps the visit before it");
        }

        var leave = Assert.Single(sent.OfType<CreateDoctorLeaveCommand>());
        Assert.Equal(DateOnly.FromDateTime(builder.LeaveDay()), leave.StartDate);
        Assert.DoesNotContain(visits, v => v.DoctorId == leave.DoctorId
            && DateOnly.FromDateTime(ClinicClock.ToClinicLocal(v.AppointmentDateTime)) == leave.StartDate);
    }

    [Theory] // [AC-2.2] every working half-day is booked edge to edge, the lunch break excepted (the series fills its own slot)
    [MemberData(nameof(Anchors))]
    public async Task Every_Working_Half_Day_Is_Fully_Booked(DateTime clinicToday)
    {
        var (seed, builder) = await BuildOn(clinicToday);
        var sent = seed.Sent.Select(s => s.Request).ToList();
        var series = Assert.Single(sent.OfType<CreateRecurringSeriesCommand>());
        var seriesDays = Enumerable.Range(0, series.OccurrenceCount!.Value)
            .Select(w => ClinicClock.ToClinicLocal(series.StartDateTime.AddDays(7 * w)).Date).ToHashSet();
        var booked = sent.OfType<CreateAppointmentCommand>()
            .GroupBy(v => ClinicClock.ToClinicLocal(v.AppointmentDateTime).Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var day in builder.AgendaDays())
        {
            var hours = DemoScenario.Hours.Single(h => h.Day == day.DayOfWeek);
            var open = (hours.BreakFrom is { } bf && hours.BreakTo is { } bt)
                ? (hours.To - hours.From - (bt - bf)).TotalMinutes
                : (hours.To - hours.From).TotalMinutes;
            var minutes = booked[day].Sum(v => v.DurationMinutes) + (seriesDays.Contains(day) ? series.DurationMinutes : 0);
            Assert.True(open == minutes, $"{day:ddd dd/MM}: {minutes} of {open} minutes booked");
        }
    }

    [Fact] // [AC-2.2] past visits carry fiches except two left for « À clôturer » and one absence
    public async Task Past_Visits_Carry_Fiches_Except_Two_To_Close_And_One_Absence()
    {
        var today = new DateTime(2026, 11, 3);
        var (seed, _) = await BuildOn(today);
        var sent = seed.Sent.Select(s => s.Request).ToList();
        var visits = sent.OfType<CreateAppointmentCommand>().ToList();
        // Only the fiches that document a booked visit; treatment séances and the owing fiche have none.
        var fiches = sent.OfType<CreateDentalRecordCommand>().Where(f => f.AppointmentId is not null).ToList();
        var statuses = sent.OfType<UpdateAppointmentCommand>().Where(u => u.Status is not null).ToList();

        var past = visits.Count(v => ClinicClock.ToClinicLocal(v.AppointmentDateTime).Date < today);
        Assert.Equal(past - 3, fiches.Count);
        Assert.Equal("NoShow", Assert.Single(statuses).Status);
        Assert.All(fiches, f => Assert.True(ClinicClock.ToClinicLocal(f.InterventionDate).Date < today));
        Assert.All(fiches, f => Assert.Equal(f.Acts.Sum(a => a.Cost), f.AmountPaid));
    }

    [Fact] // [FR-6] every part-A area is written, and each by the member who would have done it
    public async Task Every_Area_Is_Written_By_The_Right_Member()
    {
        var (seed, _) = await BuildOn(new DateTime(2026, 11, 3));
        string RoleOf<T>() => seed.Sent.First(s => s.Request is T).Member.Role;

        Assert.Equal(2, seed.Sent.Count(s => s.Request is CreateClinicUserCommand));
        Assert.Equal("admin", RoleOf<UpdateClinicCommand>());
        Assert.Equal("admin", RoleOf<UpdateClinicLetterheadCommand>());
        Assert.Equal("admin", RoleOf<UpdateDoctorProfileCommand>());
        Assert.Equal("secretary", RoleOf<CreatePatientCommand>());
        Assert.Equal("secretary", RoleOf<CreateAppointmentCommand>());
        Assert.Equal("secretary", RoleOf<CreateRecurringSeriesCommand>());
        Assert.Equal("secretary", RoleOf<CreateWaitingListEntryCommand>());
        Assert.Contains(seed.Sent, s => s.Request is DiagnoseToothCommand);
        Assert.All(seed.Sent.Where(s => s.Request is CreateDentalRecordCommand), s => Assert.NotEqual("secretary", s.Member.Role));
        Assert.Equal(DemoScenarioPatients.All.Count, seed.Sent.Count(s => s.Request is CreatePatientCommand));
    }

    [Fact] // [FR-6] the hours pass the product's own validator, Sunday closed and a lunch break on weekdays
    public async Task The_Hours_Are_Valid_With_Sunday_Closed()
    {
        var (seed, _) = await BuildOn(new DateTime(2026, 11, 3));
        var json = seed.Sent.Select(s => s.Request).OfType<UpdateClinicCommand>().Single().WorkingHoursJson;

        var days = WorkingHoursSerializer.Validate(WorkingHoursSerializer.Parse(json));

        Assert.True(days.IsSuccess, days.Error);
        Assert.False(days.Value!.Single(d => d.Day == "Sunday").Enabled);
        Assert.Equal("13:00", days.Value!.Single(d => d.Day == "Monday").BreakFrom);
    }

    [Theory] // [AC-2.4] any day of the year — [FR-6] a devis at every state, the month's expenses, one avoir, cheques banked but one, one patient owing
    [MemberData(nameof(Anchors))]
    public async Task Treatments_And_Money_Are_Written(DateTime clinicToday)
    {
        var (seed, _) = await BuildOn(clinicToday);
        var sent = seed.Sent.Select(s => s.Request).ToList();

        Assert.Equal(4, sent.OfType<CreateTreatmentPlanCommand>().Count());
        Assert.Single(sent.OfType<StartTreatmentCommand>());
        Assert.Single(sent.OfType<StopTreatmentPlanCommand>());
        Assert.Contains(sent.OfType<CreateDentalRecordCommand>(), f => f.Acts.Any(a => a.PonticToothNumbers.SequenceEqual(new[] { 15 })));
        Assert.All(sent.OfType<CreateDentalRecordCommand>().Where(f => f.TreatmentPlanId is not null), f => Assert.All(f.Acts, a => Assert.Equal(0m, a.Cost)));
        Assert.Equal(6, sent.OfType<CreateExpenseCommand>().Count(e => e.RepeatMonthly));
        Assert.Single(sent.OfType<CreateCreditNoteCommand>());
        Assert.Contains(sent.OfType<CreateDentalRecordCommand>(), f => f.AmountPaid > 0 && f.AmountPaid < f.Acts.Sum(a => a.Cost));
        Assert.Contains(sent.OfType<CreateDentalRecordCommand>(), f => f.PaymentMethod == "Cheque" && f.ChequeNumber is not null);
    }

    [Theory] // [AC-2.4] any day of the year — no payment, expense, refund or séance is dated after the build day
    [MemberData(nameof(Anchors))]
    public async Task Nothing_Is_Paid_In_The_Future(DateTime clinicToday)
    {
        var today = clinicToday;
        var (seed, _) = await BuildOn(today);
        var sent = seed.Sent.Select(s => s.Request).ToList();
        var endOfToday = ClinicClock.ToUtc(today.AddDays(1));

        Assert.All(sent.OfType<CreateDentalRecordCommand>(), f => Assert.True(f.InterventionDate < endOfToday));
        Assert.All(sent.OfType<CreateExpenseCommand>(), e => Assert.True(e.ExpenseDate < endOfToday));
        Assert.All(sent.OfType<CreateCreditNoteCommand>(), c => Assert.True(c.RefundedOn < endOfToday));
        Assert.All(sent.OfType<CreateMedicalDocumentCommand>(), d => Assert.True(d.DocumentDate < endOfToday));
        Assert.All(sent.OfType<CreateLabWorkOrderCommand>(), l => Assert.True(l.SentDate < endOfToday));
    }

    [Theory] // [AC-2.4] any day of the year — [FR-6] documents, files, stock, fournisseurs, a late lab order and a « suite à planifier » are written
    [MemberData(nameof(Anchors))]
    public async Task Records_Are_Written(DateTime clinicToday)
    {
        var today = clinicToday;
        var (seed, _) = await BuildOn(today);
        var sent = seed.Sent.Select(s => s.Request).ToList();

        var ordonnance = Assert.Single(sent.OfType<CreateDentalRecordCommand>(), f => f.Prescription is not null).Prescription!;
        Assert.Contains(ordonnance.Lines, l => l.Kind == "medicament" && l.Name == "Clindamycine");
        Assert.Contains(ordonnance.Lines, l => l.Kind == "examen");
        Assert.DoesNotContain(ordonnance.Lines, l => l.Name?.Contains("cilline", StringComparison.OrdinalIgnoreCase) == true);

        Assert.Equal(new[] { "arret-travail", "certificat" }, sent.OfType<CreateMedicalDocumentCommand>().Select(d => d.DocumentType).Order());
        Assert.All(seed.Sent.Where(s => s.Request is CreateMedicalDocumentCommand), s => Assert.NotEqual("secretary", s.Member.Role));
        Assert.All(sent.OfType<CreateMedicalDocumentCommand>(), d => Assert.NotNull(d.IssuingDoctorId));

        var uploads = sent.OfType<UploadPatientFileCommand>().ToList();
        Assert.Equal(4, uploads.Count);
        Assert.All(uploads, u => Assert.True(u.FileSize > 10_000)); // the real embedded image, not an empty stream

        Assert.Equal(2, sent.OfType<CreateSupplierCommand>().Count());
        Assert.Contains(sent.OfType<CreateStockItemCommand>(), i => i.CurrentStock < i.MinimumStockLevel);
        var restock = Assert.Single(sent.OfType<RestockStockItemCommand>());
        Assert.True(restock.ExpiryDate > today && restock.ExpiryDate < today.AddDays(60));

        var lab = sent.OfType<CreateLabWorkOrderCommand>().ToList();
        Assert.Contains(lab, l => l.ExpectedDate < today);
        Assert.Contains(lab, l => l.ExpectedDate > today);
        Assert.All(lab, l => Assert.NotNull(l.SupplierId));

        Assert.Contains(sent.OfType<CreateDentalRecordCommand>(), f => f.TreatmentPlanId is null && f.Acts.Any(a => a.IsUnfinished));
    }

    [Fact] // [FR-6] Ross Geller carries his pénicilline allergy and asthma, as in the videos; one child is « denture mixte »
    public async Task The_Videos_Patients_Are_There()
    {
        var (seed, _) = await BuildOn(new DateTime(2026, 11, 3));
        var patients = seed.Sent.Select(s => s.Request).OfType<CreatePatientCommand>().ToList();

        var ross = Assert.Single(patients, p => p is { FirstName: "Ross", LastName: "Geller" });
        Assert.Equal("Pénicilline", ross.Allergies);
        Assert.Equal("Asthme", ross.MedicalHistory);
        Assert.Contains(patients, p => p.Dentition == "Mixed");
    }

    [Fact] // every patient a visitor opens is a full file — contact, address, motif, antécédents — and has work recorded
    public async Task Every_Patient_Is_A_Full_File_With_Work_Recorded()
    {
        var (seed, _) = await BuildOn(new DateTime(2026, 11, 3));
        var sent = seed.Sent.Select(s => s.Request).ToList();
        var patients = sent.OfType<CreatePatientCommand>().ToList();

        Assert.All(patients, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.PhoneNumber));
            Assert.EndsWith("@example.com", p.Email);
            Assert.False(string.IsNullOrWhiteSpace(p.Address?.Street));
            Assert.False(string.IsNullOrWhiteSpace(p.ConsultationReason));
            Assert.NotEmpty(p.MedicalHistoryEntries!);
            Assert.NotEmpty(p.FamilyHistoryEntries!);
        });
        Assert.Equal(patients.Count, patients.Select(p => p.PhoneNumber).Distinct().Count());

        // Every patient has at least one visit recorded on a fiche, so any odontogram and history a visitor opens has work in it.
        var fichedPatients = sent.OfType<CreateDentalRecordCommand>().Where(f => f.AppointmentId is not null).Select(f => f.PatientId).Distinct().Count();
        Assert.Equal(patients.Count, fichedPatients);
    }

    [Fact] // [FR-6] business dates come from the build day, never from the clock
    public void The_Scenario_Never_Reads_The_Clock()
    {
        var folder = Path.Combine(SolutionSources.Root().FullName, "ClinicManagement.Application", "Features", "Demo");
        var offenders = Directory.GetFiles(folder, "*.cs")
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"DateTime\.(UtcNow|Now|Today)\b|ClinicToday\("))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(Directory.GetFiles(folder, "*.cs").Length >= 3, "The scan found no scenario files.");
        Assert.True(offenders.Count == 0, "Scenario reads the clock: " + string.Join(", ", offenders));
    }
    [Theory] // the dashboard's 30 days read about 3 000 DT net, the 7 days carry no monthly charge, and nothing is spent today
    [MemberData(nameof(Anchors))]
    public async Task Net_Lands_Near_3000_And_Nothing_Is_Spent_Today(DateTime clinicToday)
    {
        var (seed, builder) = await BuildOn(clinicToday);
        var expenses = seed.Sent.Select(s => s.Request).OfType<CreateExpenseCommand>().ToList();
        var read = Assert.Single(seed.Sent.Select(s => s.Request).OfType<GetDashboardQuery>());

        Assert.Equal(DashboardPeriodKey.Month, read.Period);
        Assert.InRange(builder.NetTarget, 3000m, 3349m);
        var bill = Assert.Single(expenses, e => e.Description == "Prothèses du mois — laboratoire Dentalab");
        Assert.Equal(9000m - builder.NetTarget, bill.Amount);
        foreach (var expense in expenses)
        {
            var daysAgo = (clinicToday.Date - expense.ExpenseDate!.Value.Date).Days;
            Assert.InRange(daysAgo, 1, 29);
            if (expense.RepeatMonthly) Assert.InRange(daysAgo, 7, 29);
        }
        Assert.True((clinicToday.Date - bill.ExpenseDate!.Value.Date).Days >= 7);
    }
}
