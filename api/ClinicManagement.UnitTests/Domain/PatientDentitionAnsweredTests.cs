using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// « Has anybody ever answered which denture this patient has? » — <c>Patient.DentitionAnsweredAtUtc</c>.
///
/// <para>
/// The column exists because <c>Dentition</c> cannot answer it: NOT NULL, entity default
/// <see cref="DentitionType.Adult"/>, so a deliberate « Définitive » and « nobody was ever asked » are the same
/// stored value. The odontogramme's « Quelle denture afficher ? » fires only when nothing can seed the chart —
/// no date of birth, nothing charted — and without this marker it came back on every reload of that patient's
/// page, however many times it had been answered.
/// </para>
///
/// <para>
/// ⚠️ <b>The test that matters is <see cref="Answering_with_the_value_already_stored_still_records_an_answer"/>.</b>
/// The first fix set the marker in <c>SetDentition</c> and left the handler's <c>!= patient.Dentition</c> guard
/// in place, so an identical-value write was a no-op and the marker was never set — which meant the nag
/// survived for exactly one of the three answers, the default one. Measured in the browser before this test
/// existed: « Mixte » stuck and « Définitive » asked again on every reload. « Was it sent? » and « did the value
/// change? » are two questions, and only the first one is an answer.
/// </para>
/// </summary>
public class PatientDentitionAnsweredTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static Patient UndatedWalkIn() =>
        new(Guid.NewGuid(), ClinicId, "Walk", "In", dateOfBirth: null, gender: "Female");

    [Fact]
    public void A_new_patient_has_never_been_asked()
    {
        var patient = UndatedWalkIn();

        Assert.Null(patient.DentitionAnsweredAtUtc);
        // The default is what makes the marker necessary: it is indistinguishable from a real answer.
        Assert.Equal(DentitionType.Adult, patient.Dentition);
    }

    [Theory]
    [InlineData(DentitionType.Child)]
    [InlineData(DentitionType.Mixed)]
    [InlineData(DentitionType.Adult)]
    public void Setting_the_dentition_records_that_it_was_answered(DentitionType answer)
    {
        var patient = UndatedWalkIn();

        patient.SetDentition(answer);

        Assert.Equal(answer, patient.Dentition);
        Assert.NotNull(patient.DentitionAnsweredAtUtc);
    }

    /// <summary>
    /// ⚠️ The one that was broken. `Adult` is already stored, so this write changes nothing about the dentition
    /// — and is still the moment somebody said what it is.
    /// </summary>
    [Fact]
    public void Answering_with_the_value_already_stored_still_records_an_answer()
    {
        var patient = UndatedWalkIn();
        Assert.Equal(DentitionType.Adult, patient.Dentition);

        patient.SetDentition(DentitionType.Adult);

        Assert.NotNull(patient.DentitionAnsweredAtUtc);
    }

    /// <summary>
    /// It is never un-answered: correcting the denture is an ordinary edit, and a null would put the prompt
    /// back on a patient whose dentition is on file.
    /// </summary>
    [Fact]
    public void Changing_the_answer_keeps_it_answered()
    {
        var patient = UndatedWalkIn();
        patient.SetDentition(DentitionType.Child);
        var first = patient.DentitionAnsweredAtUtc;

        patient.SetDentition(DentitionType.Mixed);

        Assert.Equal(DentitionType.Mixed, patient.Dentition);
        Assert.NotNull(patient.DentitionAnsweredAtUtc);
        Assert.True(patient.DentitionAnsweredAtUtc >= first);
    }
}
