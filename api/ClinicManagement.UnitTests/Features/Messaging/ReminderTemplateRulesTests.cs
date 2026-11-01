using ClinicManagement.Application.Features.Messaging;

namespace ClinicManagement.UnitTests.Features.Messaging;

/// <summary>
/// What a cabinet may save as its reminder wording and timing. Each refusal is checked by its code, never its prose.
/// </summary>
public class ReminderTemplateRulesTests
{
    private const string Clinic = "Cabinet El Menzah";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Template_Means_The_Default_And_Is_Accepted(string? template) =>
        Assert.Null(ReminderTemplateRules.CheckTemplate(template, Clinic));

    [Fact]
    public void The_Default_Wording_Is_Accepted() =>
        Assert.Null(ReminderTemplateRules.CheckTemplate(ReminderText.DefaultTemplate, Clinic));

    [Fact]
    public void An_Unknown_Variable_Is_Refused_And_Named()
    {
        var refusal = ReminderTemplateRules.CheckTemplate("Bonjour {nom}, RDV le {date}.", Clinic);

        Assert.Equal(ReminderTemplateRules.UnknownVariableCode, refusal?.Code);
        Assert.Contains("{nom}", refusal!.Sentence);
    }

    [Fact]
    public void A_Template_Without_The_Date_Is_Refused()
    {
        var refusal = ReminderTemplateRules.CheckTemplate("Bonjour {patient}, a bientot chez {clinic}.", Clinic);

        Assert.Equal(ReminderTemplateRules.NeedsDateCode, refusal?.Code);
    }

    [Fact]
    public void Free_Text_And_Line_Breaks_Pushing_Past_One_Sms_Are_Refused()
    {
        var template = "Rappel : {patient}, RDV le {date} chez {clinic}.\n"
            + "Merci de venir 15 minutes en avance avec votre carte CNAM et vos radios.\n"
            + "En cas d'empechement, prevenez-nous 24 h avant.";

        var refusal = ReminderTemplateRules.CheckTemplate(template, Clinic);

        Assert.Equal(ReminderTemplateRules.TooLongCode, refusal?.Code);
        Assert.Contains(ReminderText.SmsMaxLength.ToString(), refusal!.Sentence);
    }

    [Fact]
    public void The_Limit_Is_Inclusive()
    {
        // Pad the doctor's text until the worst case is exactly one SMS.
        var template = "{patient} {date} ";
        while (ReminderText.WorstCaseLength(template + "x", Clinic) <= ReminderText.SmsMaxLength)
        {
            template += "x";
        }

        Assert.Equal(ReminderText.SmsMaxLength, ReminderText.WorstCaseLength(template, Clinic));
        Assert.Null(ReminderTemplateRules.CheckTemplate(template, Clinic));
        Assert.Equal(ReminderTemplateRules.TooLongCode, ReminderTemplateRules.CheckTemplate(template + "x", Clinic)?.Code);
    }

    [Fact]
    public void The_Acte_Variable_Reserves_Room_For_The_Acts()
    {
        const string without = "{patient} {date}";
        const string with = "{patient} {date} {acte}";

        Assert.Equal(
            ReminderText.WorstCaseLength(without, Clinic) + 1 + ReminderText.ReservedActeLength,
            ReminderText.WorstCaseLength(with, Clinic));
    }

    [Fact]
    public void A_Longer_Clinic_Name_Leaves_Less_Room_For_The_Doctors_Text()
    {
        const string template = "{patient}, RDV le {date} chez {clinic}.";

        Assert.True(
            ReminderText.WorstCaseLength(template, "Cabinet Dentaire International du Lac") >
            ReminderText.WorstCaseLength(template, "Cabinet"));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 24 })]
    [InlineData(new[] { 48, 2 })]
    public void Allowed_Lead_Times_Are_Accepted(int[] hours) =>
        Assert.Null(ReminderTemplateRules.CheckLeadTimes(hours));

    [Fact]
    public void No_Lead_Times_Means_The_Installation_Default() =>
        Assert.Null(ReminderTemplateRules.CheckLeadTimes(null));

    [Theory]
    [InlineData(new[] { 72 })]
    [InlineData(new[] { 24, 12, 6 })]
    [InlineData(new[] { 0 })]
    public void Lead_Times_Outside_The_Choices_Or_More_Than_Two_Are_Refused(int[] hours) =>
        Assert.Equal(ReminderTemplateRules.LeadTimeCode, ReminderTemplateRules.CheckLeadTimes(hours)?.Code);
}
