using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// « Which open diagnoses does a séance actually answer? »
///
/// <para>
/// The defect these pin was silent and clinical, and it is the reason the odontogram could not safely draw
/// faces: <c>ClearDiagnosesForTreatedTeethAsync</c> matched on the tooth number alone, so restoring the
/// <b>mésiale</b> of 26 hard-deleted the diagnosis recorded on its <b>occlusale</b>. Nothing failed — the chart
/// simply stopped saying the second face was still to do, and « N dents à traiter » lost a tooth that still
/// needed work.
/// </para>
///
/// <para>
/// ⚠️ The rule is deliberately conservative and these cases say why: an unfaced treatment still closes
/// everything on its tooth, and an unfaced diagnosis is still closed by faced work — otherwise « À traiter »
/// and « Fracture », which name no place, could never be closed by anything and would accumulate forever.
/// </para>
/// </summary>
public class DiagnosisClosureBySurfaceTests
{
    private static readonly Guid Patient = Guid.NewGuid();
    private static readonly Guid Clinic = Guid.NewGuid();

    private static ToothState Diagnosis(int tooth, string? surfaces) =>
        new(Guid.NewGuid(), Patient, Clinic, tooth, ToothCondition.Carie, DateTime.UtcNow,
            surfaces, null, null, ToothStateSource.Diagnosis);

    private static ToothState Treatment(int tooth, string? surfaces) =>
        new(Guid.NewGuid(), Patient, Clinic, tooth, ToothCondition.Obturation, DateTime.UtcNow,
            surfaces, null, null, ToothStateSource.Treatment);

    /// <summary>Runs the closure and reports which diagnosis ids were deleted.</summary>
    private static async Task<List<Guid>> Close(IReadOnlyList<ToothState> existing, IReadOnlyList<ToothState> treated)
    {
        var deleted = new List<Guid>();
        var repo = new Mock<IToothStateRepository>();
        repo.Setup(r => r.GetByPatientIdAsync(Patient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        repo.Setup(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => deleted.Add(id))
            .Returns(Task.CompletedTask);

        await DentalRecordLinker.ClearDiagnosesForTreatedTeethAsync(repo.Object, Patient, treated, default);
        return deleted;
    }

    /// <summary>The defect itself: treating one face must not close a diagnosis naming another.</summary>
    [Fact]
    public async Task Treating_one_face_leaves_a_diagnosis_on_another_face_open()
    {
        var carie = Diagnosis(26, "MO");

        var deleted = await Close(new[] { carie }, new[] { Treatment(26, "M") });

        Assert.Empty(deleted);
    }

    [Fact]
    public async Task Treating_every_diagnosed_face_closes_it()
    {
        var carie = Diagnosis(26, "MO");

        var deleted = await Close(new[] { carie }, new[] { Treatment(26, "MOD") });

        Assert.Equal(new[] { carie.Id }, deleted);
    }

    /// <summary>The union is per tooth and per SÉANCE — two acts covering M and O together answer a carie MO.</summary>
    [Fact]
    public async Task Two_acts_in_one_seance_union_their_faces()
    {
        var carie = Diagnosis(26, "MO");

        var deleted = await Close(new[] { carie }, new[] { Treatment(26, "M"), Treatment(26, "O") });

        Assert.Equal(new[] { carie.Id }, deleted);
    }

    /// <summary>An extraction names no face and ends everything on the tooth.</summary>
    [Fact]
    public async Task An_unfaced_treatment_closes_a_faced_diagnosis()
    {
        var carie = Diagnosis(26, "MOD");

        var deleted = await Close(new[] { carie }, new[] { Treatment(26, null) });

        Assert.Equal(new[] { carie.Id }, deleted);
    }

    /// <summary>
    /// ⚠️ An unfaced act must not be narrowed by a faced sibling on the same tooth. A fiche recording an
    /// extraction and a filling on 26 has still ended every diagnosis there.
    /// </summary>
    [Fact]
    public async Task An_unfaced_act_is_not_narrowed_by_a_faced_one_on_the_same_tooth()
    {
        var carie = Diagnosis(26, "MOD");

        var deleted = await Close(new[] { carie }, new[] { Treatment(26, "M"), Treatment(26, null) });

        Assert.Equal(new[] { carie.Id }, deleted);
    }

    /// <summary>
    /// A diagnosis naming no face is still closed by faced work — « À traiter » and « Fracture » name no place,
    /// and withholding closure would leave them open forever and inflate « N dents à traiter ».
    /// </summary>
    [Fact]
    public async Task An_unfaced_diagnosis_is_closed_by_faced_work()
    {
        var aTraiter = Diagnosis(26, null);

        var deleted = await Close(new[] { aTraiter }, new[] { Treatment(26, "O") });

        Assert.Equal(new[] { aTraiter.Id }, deleted);
    }

    [Fact]
    public async Task A_diagnosis_on_another_tooth_is_untouched()
    {
        var here = Diagnosis(26, "O");
        var elsewhere = Diagnosis(27, "O");

        var deleted = await Close(new[] { here, elsewhere }, new[] { Treatment(26, "O") });

        Assert.Equal(new[] { here.Id }, deleted);
    }

    /// <summary>A treatment entry is never closed by this path — only its own fiche can correct it.</summary>
    [Fact]
    public async Task A_treatment_entry_is_never_deleted()
    {
        var priorWork = Treatment(26, "O");

        var deleted = await Close(new[] { priorWork }, new[] { Treatment(26, "O") });

        Assert.Empty(deleted);
    }

    [Fact]
    public void Resolves_is_the_one_rule_and_reads_the_three_cases_directly()
    {
        Assert.True(ToothSurfaces.Resolves(ToothSurfaces.Parse(null), ToothSurfaces.Parse("MOD")));
        Assert.True(ToothSurfaces.Resolves(ToothSurfaces.Parse("O"), ToothSurfaces.Parse(null)));
        Assert.True(ToothSurfaces.Resolves(ToothSurfaces.Parse("MOD"), ToothSurfaces.Parse("MO")));
        Assert.False(ToothSurfaces.Resolves(ToothSurfaces.Parse("M"), ToothSurfaces.Parse("MO")));
    }

    /// <summary>
    /// The normaliser had a byte-for-byte twin in two entities; both now delegate here, so one refusal and one
    /// alphabet. Pinned so a re-inlined copy shows up as a behavioural difference rather than as tidy code.
    /// </summary>
    [Fact]
    public void Both_entities_normalise_surfaces_through_the_shared_rule()
    {
        var state = new ToothState(Guid.NewGuid(), Patient, Clinic, 26, ToothCondition.Carie, DateTime.UtcNow, " mo ");
        Assert.Equal("MO", state.Surfaces);

        var act = new DentalRecordAct(Guid.NewGuid(), Guid.NewGuid(), new DentalRecordActInput(
            null, "Obturation", 90m, null, false, new List<int> { 26 }, ToothCondition.Obturation, " mo ", null));
        Assert.Equal("MO", act.Surfaces);

        Assert.Throws<ArgumentException>(() =>
            new ToothState(Guid.NewGuid(), Patient, Clinic, 26, ToothCondition.Carie, DateTime.UtcNow, "X"));
    }
}
