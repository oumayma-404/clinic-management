using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// « À encaisser » (`seance-au-fauteuil`): the day's séances whose fiche is saved and whose money the desk still has
/// to take. The dentist enters the acts and leaves « Payé » empty; this is the secretary's list. Every case is a
/// question the desk must answer the same way « À clôturer » does — what is owed, and what is not the desk's.
/// </summary>
public class GetSeancesToCollectQueryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string Day = "2026-11-18";
    private static readonly DateTime TenOClock = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc); // 10:00 in Tunis

    private sealed class Harness
    {
        public Mock<IAppointmentRepository> Appointments { get; } = new();
        public Mock<IDentalRecordRepository> Records { get; } = new();
        public Mock<IInvoiceRepository> Invoices { get; } = new();
        public Mock<ITreatmentPlanRepository> Plans { get; } = new();
        public Mock<IPatientRepository> Patients { get; } = new();
        public Mock<IDoctorRepository> Doctors { get; } = new();
        public Mock<ICurrentClinicResolver> Resolver { get; } = new();

        private readonly List<Appointment> _visits = new();
        private readonly List<(Guid AppointmentId, Guid DentalRecordId, decimal Cost)> _fiches = new();
        private readonly List<(Guid AppointmentId, Guid InvoiceId, string? Number, InvoiceStatus Status)> _visitNotes = new();
        private readonly List<TreatmentPlan> _plans = new();
        private readonly List<Guid> _debtItems = new();

        public Harness()
        {
            Resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<Guid>.Success(ClinicId));
            Appointments.Setup(r => r.GetClosureCandidatesAsync(
                    ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _visits);
            Records.Setup(r => r.GetAppointmentLinksAsync(
                    ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _fiches);
            Invoices.Setup(r => r.GetAppointmentLinksAsync(
                    ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _visitNotes);
            Invoices.Setup(r => r.GetDentalRecordLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());
            Plans.Setup(r => r.GetDebtBearingItemIdsAsync(
                    ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _debtItems);
            Plans.Setup(r => r.GetByPatientIdsAsync(
                    ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _plans);
            Invoices.Setup(r => r.GetByPatientIdsAsync(
                    ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<Invoice>());
            Patients.Setup(r => r.GetByIdsAsync(ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _visits
                    .Where(v => v.PatientId.HasValue)
                    .ToDictionary(v => v.PatientId!.Value, v => new Patient(
                        v.PatientId!.Value, ClinicId, "Amine", "Trabelsi", new DateTime(1985, 3, 2), "Homme")));
            Doctors.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<Doctor>());
        }

        public Appointment Visit(decimal? ficheCost)
        {
            var visit = new Appointment(Guid.NewGuid(), ClinicId, Guid.NewGuid(), null, TenOClock, TimeSpan.FromHours(1));
            _visits.Add(visit);
            if (ficheCost is { } cost) _fiches.Add((visit.Id, Guid.NewGuid(), cost));
            return visit;
        }

        public Invoice NoteFor(Appointment visit, decimal total, decimal paid, bool issued = true)
        {
            var invoice = new Invoice(Guid.NewGuid(), ClinicId, visit.PatientId!.Value, appointmentId: visit.Id);
            invoice.SetLines(new[] { ("Soin", 1, total) });
            if (issued)
            {
                invoice.Issue("2026-0142");
                if (paid > 0) invoice.RecordPayment(paid, PaymentMethod.Cash, TenOClock);
            }
            _visitNotes.Add((visit.Id, invoice.Id, invoice.Number, invoice.Status));
            Invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
            return invoice;
        }

        /// <summary>The visit carries an act of an accepted devis; <paramref name="dueNow"/> of it is late.</summary>
        public TreatmentPlan DevisFor(Appointment visit, decimal dueNow)
        {
            var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, visit.PatientId!.Value, "Réhabilitation");
            plan.SetItems(new[] { ("Couronne", 1000m, (IReadOnlyList<int>)new[] { 11 }) });
            var farAhead = new DateTime(2099, 1, 10, 0, 0, 0, DateTimeKind.Utc);
            plan.SetInstallments(dueNow > 0m
                ? new[] { (new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), dueNow), (farAhead, 1000m - dueNow) }
                : new[] { (farAhead, 1000m) });
            plan.Accept("2026-0014");
            var itemId = plan.Items.First().Id;
            visit.SetTreatmentPlanItem(itemId);
            _plans.Add(plan);
            _debtItems.Add(itemId);
            return plan;
        }

        public async Task<IReadOnlyList<SeanceToCollectDto>> Read()
        {
            var handler = new GetSeancesToCollectQueryHandler(
                Appointments.Object, Records.Object, Invoices.Object, Plans.Object, Patients.Object, Doctors.Object,
                Resolver.Object, NullLogger<GetSeancesToCollectQueryHandler>.Instance);
            var result = await handler.Handle(new GetSeancesToCollectQuery { Day = Day }, CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
            return result.Value!;
        }
    }

    // The headline: the dentist saved the fiche with « Payé » empty, so the séance waits for the desk at its total.
    [Fact]
    public async Task A_Saved_Fiche_With_No_Note_Is_To_Collect_At_Its_Total()
    {
        var h = new Harness();
        var visit = h.Visit(ficheCost: 120m);

        var row = Assert.Single(await h.Read());

        Assert.Equal(visit.Id, row.AppointmentId);
        Assert.Equal(SeanceCollectionKinds.Unbilled, row.Kind);
        Assert.Equal(120m, row.AmountDue);
        Assert.Null(row.InvoiceId);
    }

    // The dentist took part of it at the chair: the desk is asked for what is left on the note, never the total.
    [Fact]
    public async Task A_Part_Paid_Note_Is_To_Collect_At_Its_Balance()
    {
        var h = new Harness();
        var visit = h.Visit(ficheCost: 120m);
        var note = h.NoteFor(visit, total: 120m, paid: 50m);

        var row = Assert.Single(await h.Read());

        Assert.Equal(SeanceCollectionKinds.NoteBalance, row.Kind);
        Assert.Equal(note.Outstanding, row.AmountDue);
        Assert.Equal(note.Id, row.InvoiceId);
    }

    [Fact]
    public async Task A_Paid_Note_Is_Not_On_The_List()
    {
        var h = new Harness();
        var visit = h.Visit(ficheCost: 120m);
        var note = h.NoteFor(visit, total: 120m, paid: 0m);
        note.RecordPayment(note.Outstanding, PaymentMethod.Cash, TenOClock);

        Assert.Empty(await h.Read());
    }

    // A draft holds no money: the desk bills the FICHE and the draft is replaced — so the amount owed is the fiche's,
    // and the draft's own total is carried only to be stated before the desk confirms (owner's review, 2026-11-19).
    [Fact]
    public async Task A_Draft_Note_Is_Named_And_The_Fiche_Is_What_Is_Owed()
    {
        var h = new Harness();
        var visit = h.Visit(ficheCost: 120m);
        var draft = h.NoteFor(visit, total: 40m, paid: 0m, issued: false);

        var row = Assert.Single(await h.Read());

        Assert.Equal(SeanceCollectionKinds.DraftNote, row.Kind);
        Assert.Equal(120m, row.AmountDue);
        Assert.Equal(40m, row.DraftTotal);
        Assert.Equal(draft.Id, row.InvoiceId);
    }

    // No fiche: nothing is priced yet — the dentist has not entered the acts.
    [Fact]
    public async Task A_Visit_Without_A_Fiche_Is_Not_On_The_List()
    {
        var h = new Harness();
        h.Visit(ficheCost: null);

        Assert.Empty(await h.Read());
    }

    // A contrôle gratuit is complete work with nothing to take.
    [Fact]
    public async Task A_Fiche_Worth_Nothing_Is_Not_On_The_List()
    {
        var h = new Harness();
        h.Visit(ficheCost: 0m);

        Assert.Empty(await h.Read());
    }

    // A devis séance: its fiche is priced 0, its money is the devis's. Listed when an échéance is due now, at that
    // figure — « Reste à payer »'s « À relancer » — so the desk collects it on the échéance, tagged with the fiche.
    [Fact]
    public async Task A_Devis_Seance_With_An_Echeance_Due_Is_To_Collect_On_The_Treatment()
    {
        var h = new Harness();
        var visit = h.Visit(ficheCost: 0m);
        var plan = h.DevisFor(visit, dueNow: 400m);

        var row = Assert.Single(await h.Read());

        Assert.Equal(SeanceCollectionKinds.Treatment, row.Kind);
        Assert.Equal(400m, row.AmountDue);
        Assert.Equal(plan.Id, row.TreatmentPlanId);
        Assert.Equal("2026-0014", row.TreatmentPlanNumber);
    }

    // Nothing late on the devis: the dentist decides that money at the chair, and the desk is not asked.
    [Fact]
    public async Task A_Devis_Seance_With_Nothing_Due_Is_Not_On_The_List()
    {
        var h = new Harness();
        var visit = h.Visit(ficheCost: 0m);
        h.DevisFor(visit, dueNow: 0m);

        Assert.Empty(await h.Read());
    }

    [Fact]
    public async Task A_Malformed_Day_Is_Refused()
    {
        var h = new Harness();
        var handler = new GetSeancesToCollectQueryHandler(
            h.Appointments.Object, h.Records.Object, h.Invoices.Object, h.Plans.Object, h.Patients.Object,
            h.Doctors.Object, h.Resolver.Object, NullLogger<GetSeancesToCollectQueryHandler>.Instance);

        var result = await handler.Handle(new GetSeancesToCollectQuery { Day = "18/11/2026" }, CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}
