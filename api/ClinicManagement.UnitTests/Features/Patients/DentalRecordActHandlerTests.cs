using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using MediatR;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// Dental-record Create/Update orchestration for the tooth-first flow (feature
/// <c>tooth-first-record-entry</c>): AC-6 (a mixed-dentition session saves and writes both tooth states),
/// AC-2 (two acts on one tooth write two odontogram entries), AC-5 (a mouth-level act writes none),
/// AC-12 (a bad amount fails before any commit) and AC-16 (an update replaces only this record's own tooth
/// states and closes diagnoses on the teeth it treats). Mirrors the Moq harness shape used by the other
/// handler tests.
/// </summary>
public class DentalRecordActHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime Intervention = new(2026, 7, 26, 9, 0, 0, DateTimeKind.Utc);

    private static Patient PatientIn(Guid clinicId) => new(
        Guid.NewGuid(), clinicId, "Jean", "Dupont",
        new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
        new Email("jean.dupont@example.com"), new PhoneNumber("+21620123456"));

    private static DentalActInput Input(
        string name = "Soin de carie / obturation",
        decimal cost = 90m,
        decimal? unitCost = 90m,
        bool isPerTooth = true,
        int[]? teeth = null,
        string? condition = "Obturation") => new()
        {
            ProcedureName = name,
            Cost = cost,
            UnitCost = unitCost,
            IsPerTooth = isPerTooth,
            ToothNumbers = (teeth ?? new[] { 16 }).ToList(),
            ResultingCondition = condition,
        };

    private sealed class Harness
    {
        public Mock<IPatientRepository> Patients { get; } = new();
        public Mock<IDentalRecordRepository> Records { get; } = new();
        public Mock<IToothStateRepository> ToothStates { get; } = new();
        public Mock<ITreatmentPlanRepository> Plans { get; } = new();

        // The update handler asks these two, pre-commit, whether a note d'honoraires already bills this fiche
        // (AC-2 / AC-3b). Stubbed to « rien ne la facture », which is every fixture in this class — and stubbed
        // rather than left bare because Moq's default for a collection-returning task is **null**, which the guard
        // would dereference into this project's catch → French-Result convention (see UnitTests/CLAUDE.md).
        public Mock<IInvoiceRepository> Invoices { get; } = new();
        public Mock<ICreditNoteRepository> CreditNotes { get; } = new();
        public Mock<IAppointmentRepository> Appointments { get; } = new();
        public Mock<ICurrentClinicResolver> Resolver { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<INotificationGenerator> Generator { get; } = new();
        public Mock<IRealtimeNotifier> Realtime { get; } = new();

        /// <summary>Tooth states the handler staged, in order.</summary>
        public List<ToothState> AddedStates { get; } = new();
        /// <summary>Tooth-state ids the handler removed (replaced entries + closed diagnoses).</summary>
        public List<Guid> DeletedStateIds { get; } = new();

        public Patient Patient { get; }

        public Harness(Guid? patientClinicId = null, IEnumerable<ToothState>? existingForPatient = null)
        {
            Patient = PatientIn(patientClinicId ?? ClinicId);
            Patients.Setup(r => r.GetByIdAsync(Patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Patient);
            Resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Success(ClinicId));
            Uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Invoices.Setup(r => r.GetDentalRecordLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());

            ToothStates.Setup(r => r.GetByPatientIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingForPatient ?? Array.Empty<ToothState>());
            ToothStates.Setup(r => r.AddAsync(It.IsAny<ToothState>(), It.IsAny<CancellationToken>()))
                .Callback<ToothState, CancellationToken>((s, _) => AddedStates.Add(s))
                .ReturnsAsync((ToothState s, CancellationToken _) => s);
            ToothStates.Setup(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Callback<Guid, CancellationToken>((id, _) => DeletedStateIds.Add(id))
                .Returns(Task.CompletedTask);
        }

        /// <summary>
        /// Stock consumption is a post-commit best-effort side effect (AC-P4.13); a bare mock is enough for
        /// these act/odontogram tests, and `StockConsumptionTests` covers the behaviour itself.
        /// </summary>
        public Mock<IStockConsumptionService> StockConsumption { get; } = new();

        /// <summary>
        /// Auto-billing is a post-commit best-effort side effect like stock consumption, so a bare sender is
        /// enough here — `DentalRecordAutoBillingTests` covers the behaviour itself. A bare `Mock&lt;ISender&gt;`
        /// returns a null `Result`, which the seam treats as a failure and reports rather than throwing; these
        /// act/odontogram tests only care that the record itself still saves.
        /// </summary>
        public Mock<ISender> Sender { get; } = new();

        // L9 — arranged to reproduce this harness's ORIGINAL behaviour: an empty roster and no caller doctor means
        // `PractitionerAttribution.Resolve` finds no candidate, so the fiche stays unattributed exactly as before.
        public Mock<IDoctorRepository> Doctors { get; } = new();
        public Mock<IClinicContext> Context { get; } = new();

        public CreateDentalRecordCommandHandler CreateHandler()
        {
            Doctors.Setup(r => r.GetByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<Doctor>());
            Context.Setup(c => c.GetUserId()).Returns((string?)null);
            return CreateHandlerCore();
        }

        private CreateDentalRecordCommandHandler CreateHandlerCore() => new(
            Patients.Object, Records.Object, ToothStates.Object, Plans.Object, Doctors.Object, Context.Object,
            Appointments.Object, Resolver.Object, Uow.Object, Generator.Object, StockConsumption.Object,
            Realtime.Object, Sender.Object, NullLogger<CreateDentalRecordCommandHandler>.Instance);

        public UpdateDentalRecordCommandHandler UpdateHandler() => new(
            Records.Object, Patients.Object, ToothStates.Object, Plans.Object, Invoices.Object,
            CreditNotes.Object, Resolver.Object, Uow.Object, StockConsumption.Object, Sender.Object,
            NullLogger<UpdateDentalRecordCommandHandler>.Instance);

        public CreateDentalRecordCommand CreateCommand(params DentalActInput[] acts) => new()
        {
            PatientId = Patient.Id,
            InterventionDate = Intervention,
            AmountPaid = 0m,
            IsAdultTeeth = true,
            Acts = acts.ToList(),
        };

        public UpdateDentalRecordCommand UpdateCommand(Guid recordId, params DentalActInput[] acts) => new()
        {
            Id = recordId,
            PatientId = Patient.Id,
            InterventionDate = Intervention,
            AmountPaid = 0m,
            IsAdultTeeth = true,
            Acts = acts.ToList(),
        };
    }

    // [AC-6] A permanent 36 and a deciduous 75 in one session: the record saves and BOTH teeth reach the
    // odontogram. This combination was rejected outright before the dentition check was relaxed.
    [Fact]
    public async Task Create_Accepts_A_Mixed_Dentition_Session_And_Writes_Both_Tooth_States()
    {
        var h = new Harness();

        var result = await h.CreateHandler().Handle(
            h.CreateCommand(
                Input(teeth: new[] { 36 }),
                Input(name: "Soin dentaire enfant (dent de lait)", cost: 60m, unitCost: 60m, teeth: new[] { 75 })),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 36, 75 }, h.AddedStates.Select(s => s.ToothNumber));
        h.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-6] The persisted record keeps both teeth and reports the derived total.
    [Fact]
    public async Task Create_Returns_A_Dto_Carrying_Both_Dentitions()
    {
        var h = new Harness();

        var result = await h.CreateHandler().Handle(
            h.CreateCommand(
                Input(cost: 90m, teeth: new[] { 36 }),
                Input(name: "Soin dentaire enfant (dent de lait)", cost: 60m, unitCost: 60m, teeth: new[] { 75 })),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 36, 75 }, result.Value!.ToothNumbers);
        Assert.Equal(150m, result.Value.Cost);
    }

    // [AC-2] Two procedures on tooth 16 in one session → two acts and two odontogram entries.
    [Fact]
    public async Task Create_With_Two_Acts_On_One_Tooth_Writes_Two_Tooth_States()
    {
        var h = new Harness();

        var result = await h.CreateHandler().Handle(
            h.CreateCommand(
                Input(name: "Traitement de canal (dévitalisation)", cost: 150m, unitCost: 150m,
                    teeth: new[] { 16 }, condition: "TraitementDeCanal"),
                Input(name: "Couronne / bridge (par élément)", cost: 500m, unitCost: 500m,
                    teeth: new[] { 16 }, condition: "Couronne")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, h.AddedStates.Count);
        Assert.All(h.AddedStates, s => Assert.Equal(16, s.ToothNumber));
        Assert.Equal(2, result.Value!.Acts.Count);
        Assert.Single(result.Value.ToothNumbers);
    }

    // [AC-3] The per-tooth pricing provenance survives the round-trip to the DTO, so reopening the record
    // shows "unit × teeth" rather than a flat total.
    [Fact]
    public async Task Create_Echoes_The_PerTooth_Pricing_Provenance()
    {
        var h = new Harness();

        var result = await h.CreateHandler().Handle(
            h.CreateCommand(Input(cost: 180m, unitCost: 90m, isPerTooth: true, teeth: new[] { 16, 26 })),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var act = Assert.Single(result.Value!.Acts);
        Assert.Equal(180m, act.Cost);
        Assert.Equal(90m, act.UnitCost);
        Assert.True(act.IsPerTooth);
    }

    // [AC-5] A mouth-level act (no tooth) saves and touches no odontogram entry.
    [Fact]
    public async Task Create_With_A_Mouth_Level_Act_Writes_No_Tooth_State()
    {
        var h = new Harness();

        var result = await h.CreateHandler().Handle(
            h.CreateCommand(Input(name: "Détartrage", cost: 60m, unitCost: 60m,
                teeth: Array.Empty<int>(), condition: null)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(h.AddedStates);
        Assert.Empty(result.Value!.ToothNumbers);
        h.ToothStates.Verify(r => r.AddAsync(It.IsAny<ToothState>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Treating a tooth closes any diagnosis charted on it, and leaves other teeth' diagnoses alone.
    [Fact]
    public async Task Create_Closes_Only_The_Diagnoses_On_The_Teeth_It_Treats()
    {
        var patientId = Guid.NewGuid();
        var treatedDiagnosis = new ToothState(
            Guid.NewGuid(), patientId, ClinicId, 16, ToothCondition.Carie, Intervention,
            source: ToothStateSource.Diagnosis);
        var untouchedDiagnosis = new ToothState(
            Guid.NewGuid(), patientId, ClinicId, 27, ToothCondition.Carie, Intervention,
            source: ToothStateSource.Diagnosis);
        var h = new Harness(existingForPatient: new[] { treatedDiagnosis, untouchedDiagnosis });

        var result = await h.CreateHandler().Handle(
            h.CreateCommand(Input(teeth: new[] { 16 })), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { treatedDiagnosis.Id }, h.DeletedStateIds);
    }

    // [AC-12] A negative amount fails before anything is written — the Result path, not an exception.
    [Fact]
    public async Task Create_Rejects_A_Negative_Cost_Without_Committing()
    {
        var h = new Harness();

        var result = await h.CreateHandler().Handle(
            h.CreateCommand(Input(cost: -1m)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Le coût de l'acte ne peut pas être négatif.", result.Error);
        h.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        h.Records.Verify(r => r.AddAsync(It.IsAny<DentalRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // An invalid FDI number still fails, so relaxing the dentition rule did not open the door to junk.
    [Fact]
    public async Task Create_Rejects_An_Invalid_Tooth_Number_Without_Committing()
    {
        var h = new Harness();

        var result = await h.CreateHandler().Handle(
            h.CreateCommand(Input(teeth: new[] { 19 })), CancellationToken.None);

        Assert.True(result.IsFailure);
        h.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Requires_At_Least_One_Act()
    {
        var h = new Harness();

        var result = await h.CreateHandler().Handle(h.CreateCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Au moins un acte est requis.", result.Error);
    }

    // Tenant isolation: a patient in another clinic reads as "not found" and nothing is written.
    [Fact]
    public async Task Create_Rejects_A_Patient_From_Another_Clinic()
    {
        var h = new Harness(patientClinicId: OtherClinicId);

        var result = await h.CreateHandler().Handle(
            h.CreateCommand(Input()), CancellationToken.None);

        Assert.True(result.IsFailure);
        h.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-16] An update replaces this record's own odontogram entries — the old ones are removed and the new
    // act's entry is written, so editing a session never leaves stale teeth behind.
    [Fact]
    public async Task Update_Replaces_Only_This_Records_Tooth_States()
    {
        var h = new Harness();
        var record = new DentalRecord(Guid.NewGuid(), h.Patient.Id, h.Patient.ClinicId, Intervention, 0m, true);
        var stale = new ToothState(Guid.NewGuid(), h.Patient.Id, h.Patient.ClinicId, 16, ToothCondition.Obturation, Intervention,
            dentalRecordId: record.Id);
        h.Records.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        h.ToothStates.Setup(r => r.GetByDentalRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { stale });

        var result = await h.UpdateHandler().Handle(
            h.UpdateCommand(record.Id, Input(name: "Extraction simple", cost: 60m, unitCost: 60m,
                teeth: new[] { 26 }, condition: "ExtraitAbsent")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { stale.Id }, h.DeletedStateIds);
        Assert.Equal(new[] { 26 }, h.AddedStates.Select(s => s.ToothNumber));
        Assert.Equal(ToothCondition.ExtraitAbsent, h.AddedStates.Single().Condition);
    }

    // [AC-6] The same relaxation applies on the edit path.
    [Fact]
    public async Task Update_Accepts_A_Mixed_Dentition_Session()
    {
        var h = new Harness();
        var record = new DentalRecord(Guid.NewGuid(), h.Patient.Id, h.Patient.ClinicId, Intervention, 0m, true);
        h.Records.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        h.ToothStates.Setup(r => r.GetByDentalRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ToothState>());

        var result = await h.UpdateHandler().Handle(
            h.UpdateCommand(record.Id,
                Input(teeth: new[] { 36 }),
                Input(name: "Soin dentaire enfant (dent de lait)", cost: 60m, unitCost: 60m, teeth: new[] { 75 })),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 36, 75 }, h.AddedStates.Select(s => s.ToothNumber));
    }

    // [AC-8] Re-saving an unchanged record leaves every act cost exactly as it was.
    [Fact]
    public async Task Update_Preserves_Act_Costs_On_A_No_Op_Save()
    {
        var h = new Harness();
        var record = new DentalRecord(Guid.NewGuid(), h.Patient.Id, h.Patient.ClinicId, Intervention, 0m, true);
        h.Records.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        h.ToothStates.Setup(r => r.GetByDentalRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ToothState>());

        var result = await h.UpdateHandler().Handle(
            h.UpdateCommand(record.Id, Input(cost: 180m, unitCost: 90m, isPerTooth: true, teeth: new[] { 16, 26 })),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var act = Assert.Single(result.Value!.Acts);
        Assert.Equal(180m, act.Cost);
        Assert.Equal(90m, act.UnitCost);
        Assert.True(act.IsPerTooth);
        Assert.Equal(180m, result.Value.Cost);
    }
}
