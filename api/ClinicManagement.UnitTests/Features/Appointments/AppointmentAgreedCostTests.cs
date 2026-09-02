using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Application.Features.ProcedureTypes;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// A price negotiated for one act at one visit, carried from the booking dialog to the fiche de soins.
///
/// <para><b>Why it exists.</b> A patient telephones and haggles. The product's answer to a negotiated price was
/// the devis, which is a document with a workflow — far too heavy for one act settled in one sentence at the
/// desk. So « Actes du rendez-vous » now carries a price per act, and the fiche prices the act at that figure
/// instead of the catalogue tarif.</para>
///
/// <para>⚠️ <b>Null is a value here, and the whole design rests on it.</b> Null means « nobody negotiated »,
/// which is what every row written before this existed means and what a booking that simply picks an act means.
/// It is <i>not</i> 0 (an act offered — a real negotiation), and it is not the catalogue's <c>DefaultCost</c>
/// copied in: substituting the tarif would freeze today's price onto every visit ever booked and make a later
/// tarif change invisible to a booking nobody had negotiated.</para>
///
/// <para>⚠️ And it is a <b>forfait</b>, never a per-tooth rate. Teeth are unknown when a visit is booked, so a
/// unit price cannot be turned back into the total the patient was quoted: told « 120 DT » for two extractions,
/// a per-tooth reading bills 240.</para>
/// </summary>
public class AppointmentAgreedCostTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Extraction = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Detartrage = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PlanItemA = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");

    private static Appointment Appointment() => new(
        Guid.NewGuid(), ClinicId, PatientId, doctorId: null,
        new DateTime(2026, 3, 12, 9, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30));

    private readonly Mock<IProcedureTypeRepository> _procedureTypes = new();

    public AppointmentAgreedCostTests()
    {
        Catalogue(Extraction, "Extraction simple", 150m);
        Catalogue(Detartrage, "Détartrage", 60m);
    }

    private void Catalogue(Guid id, string name, decimal? defaultCost)
    {
        var procedureType = new ProcedureType(
            id, ClinicId, name, 30, ColorHex.FromString("#4F83CC"), defaultCost: defaultCost);
        _procedureTypes
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(procedureType);
    }

    private Task<Application.Common.Models.Result<List<AppointmentProcedureInput>>> Resolve(
        params AppointmentProcedureRequest[] requested) =>
        AppointmentProcedureSelection.ResolveAsync(
            _procedureTypes.Object, ClinicId, requested,
            new Dictionary<Guid, string> { [PlanItemA] = "Facette céramique" },
            CancellationToken.None);

    // ── the domain row ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_Act_Booked_Without_A_Negotiation_Carries_No_Agreed_Price()
    {
        var appointment = Appointment();

        appointment.SetProcedures(new[]
        {
            new AppointmentProcedureInput(Extraction, "Extraction simple", 30, "#4F83CC", null, null),
        });

        Assert.Null(Assert.Single(appointment.Procedures).AgreedCost);
    }

    [Fact]
    public void SetProcedures_Carries_The_Agreed_Price_Onto_The_Row()
    {
        var appointment = Appointment();

        appointment.SetProcedures(new[]
        {
            new AppointmentProcedureInput(Extraction, "Extraction simple", 30, "#4F83CC", 120m, null),
        });

        Assert.Equal(120m, Assert.Single(appointment.Procedures).AgreedCost);
    }

    /// <summary>
    /// Each act keeps its <b>own</b> price. The parent's scalars are a derived snapshot of the first row, and a
    /// price is deliberately not among them: « détartrage offert, extraction à 120 » is one séance with two
    /// different negotiations, and a single lead-act figure could only tell half of it.
    /// </summary>
    [Fact]
    public void Two_Acts_Of_One_Seance_Keep_Their_Own_Prices()
    {
        var appointment = Appointment();

        appointment.SetProcedures(new[]
        {
            new AppointmentProcedureInput(Detartrage, "Détartrage", 30, "#4F83CC", 0m, null),
            new AppointmentProcedureInput(Extraction, "Extraction simple", 30, "#2A9D8F", 120m, null),
        });

        var rows = appointment.Procedures.OrderBy(p => p.SequenceNumber).ToList();
        Assert.Equal(0m, rows[0].AgreedCost);
        Assert.Equal(120m, rows[1].AgreedCost);
    }

    /// <summary>
    /// Zero is a negotiation — an act offered — and must stay distinguishable from « nobody negotiated ». If the
    /// row stored 0 for both, a gesture commercial would silently reappear on the fiche at its full tarif.
    /// </summary>
    [Fact]
    public void A_Free_Act_Is_A_Negotiated_Zero_Not_An_Absent_Price()
    {
        var appointment = Appointment();

        appointment.SetProcedures(new[]
        {
            new AppointmentProcedureInput(Detartrage, "Détartrage", 30, "#4F83CC", 0m, null),
        });

        var row = Assert.Single(appointment.Procedures);
        Assert.NotNull(row.AgreedCost);
        Assert.Equal(0m, row.AgreedCost);
    }

    [Fact]
    public void A_Negative_Agreed_Price_Is_Refused_By_The_Row_Itself()
    {
        var appointment = Appointment();

        Assert.Throws<ArgumentException>(() => appointment.SetProcedures(new[]
        {
            new AppointmentProcedureInput(Extraction, "Extraction simple", 30, "#4F83CC", -1m, null),
        }));
    }

    /// <summary>
    /// Renaming or recolouring a catalogue act must not touch what was agreed for it on a booked visit — the
    /// price is the cabinet's word to a patient, not a snapshot of the catalogue.
    /// </summary>
    [Fact]
    public void Refreshing_A_Renamed_Acts_Snapshot_Leaves_The_Agreed_Price_Alone()
    {
        var appointment = Appointment();
        appointment.SetProcedures(new[]
        {
            new AppointmentProcedureInput(Extraction, "Extraction simple", 30, "#4F83CC", 120m, null),
        });

        appointment.RefreshProcedureSnapshot(Extraction, "Extraction (une racine)", "#E76F51");

        var row = Assert.Single(appointment.Procedures);
        Assert.Equal("Extraction (une racine)", row.ProcedureName);
        Assert.Equal(120m, row.AgreedCost);
    }

    // ── the request, validated rather than trusted ──────────────────────────────────────────────────────

    /// <summary>
    /// The agreed price is the one thing about an act the client is the source of, so it is the one thing that
    /// has to be checked. Name, duration and colour still come from the catalogue.
    /// </summary>
    [Fact]
    public async Task Resolve_Passes_The_Clients_Price_Through_Untouched()
    {
        var result = await Resolve(new AppointmentProcedureRequest
        {
            ProcedureTypeId = Extraction,
            AgreedCost = 120m,
        });

        Assert.True(result.IsSuccess);
        var only = Assert.Single(result.Value!);
        Assert.Equal(120m, only.AgreedCost);
        // Still the catalogue's, not the client's.
        Assert.Equal("Extraction simple", only.ProcedureName);
    }

    /// <summary>
    /// ⚠️ Absent stays absent. Falling back to <c>procedureType.DefaultCost</c> here is the tempting mistake: it
    /// would make every booking a negotiation at today's tarif, so raising a price would never reach a visit
    /// booked before the change.
    /// </summary>
    [Fact]
    public async Task Resolve_Does_Not_Substitute_The_Catalogue_Tarif_For_An_Absent_Price()
    {
        var result = await Resolve(new AppointmentProcedureRequest { ProcedureTypeId = Extraction });

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(result.Value!).AgreedCost);
    }

    [Fact]
    public async Task Resolve_Refuses_A_Negative_Price_In_French()
    {
        var result = await Resolve(new AppointmentProcedureRequest
        {
            ProcedureTypeId = Extraction,
            AgreedCost = -5m,
        });

        Assert.True(result.IsFailure);
        Assert.Equal(AppointmentProcedureSelection.AgreedCostNegative, result.Error);
    }

    /// <summary>
    /// Above the <c>decimal(18,3)</c> column's own capacity. Without this the handler accepts it, PostgreSQL
    /// refuses the write, and the dentist reads an English EF sentence — the failure
    /// <see cref="ProcedureTypeRefusals"/> was written for, here for the same column.
    /// </summary>
    [Fact]
    public async Task Resolve_Refuses_A_Price_The_Money_Column_Cannot_Hold()
    {
        var result = await Resolve(new AppointmentProcedureRequest
        {
            ProcedureTypeId = Extraction,
            AgreedCost = ProcedureTypeRefusals.MaxCost + 1m,
        });

        Assert.True(result.IsFailure);
        Assert.Equal(AppointmentProcedureSelection.AgreedCostTooLarge, result.Error);
    }

    /// <summary>
    /// ⚠️ The link-only branch returns before the catalogue lookup, so a guard placed after it would validate
    /// only the acts that happen to have a <c>ProcedureType</c> — and a hand-typed devis line could carry a
    /// negative price straight to the database.
    /// </summary>
    [Fact]
    public async Task Resolve_Validates_A_Link_Only_Acts_Price_Too()
    {
        var result = await Resolve(new AppointmentProcedureRequest
        {
            ProcedureTypeId = null,
            TreatmentPlanItemId = PlanItemA,
            AgreedCost = -1m,
        });

        Assert.True(result.IsFailure);
        Assert.Equal(AppointmentProcedureSelection.AgreedCostNegative, result.Error);
    }

    [Fact]
    public async Task Resolve_Carries_A_Link_Only_Acts_Price()
    {
        var result = await Resolve(new AppointmentProcedureRequest
        {
            ProcedureTypeId = null,
            TreatmentPlanItemId = PlanItemA,
            AgreedCost = 400m,
        });

        Assert.True(result.IsSuccess);
        var only = Assert.Single(result.Value!);
        Assert.Equal(400m, only.AgreedCost);
        Assert.Equal("Facette céramique", only.ProcedureName);
    }

    /// <summary>
    /// The prices survive the trip to the DTOs the dialogs read back. Without this the edit dialog reopens a
    /// negotiated visit showing catalogue tarifs and its next save — a reschedule, a changed note, anything —
    /// writes those tarifs over what was agreed, because the list replaces the acts.
    /// </summary>
    [Fact]
    public void The_Read_Side_Reports_Each_Acts_Agreed_Price()
    {
        var appointment = Appointment();
        appointment.SetProcedures(new[]
        {
            new AppointmentProcedureInput(Detartrage, "Détartrage", 30, "#4F83CC", null, null),
            new AppointmentProcedureInput(Extraction, "Extraction simple", 30, "#2A9D8F", 120m, null),
        });

        var dtos = appointment.ToProcedureDtos().OrderBy(d => d.SequenceNumber).ToList();

        Assert.Null(dtos[0].AgreedCost);
        Assert.Equal(120m, dtos[1].AgreedCost);
    }
}
