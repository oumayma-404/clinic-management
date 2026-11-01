using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Messaging;

namespace ClinicManagement.UnitTests.Features.Messaging;

/// <summary>
/// The one renderer of a patient reminder: what it says, in which alphabet, and that it never exceeds one SMS
/// (157 characters at Tunisie SMS) — shrinking a name or an act, never the date or the doctor's own text.
/// </summary>
public class ReminderTextTests
{
    // 14:30 in Tunis on 21 October 2026 (UTC+1).
    private static readonly DateTime Appointment = ClinicClock.ToUtc(new DateTime(2026, 10, 21, 14, 30, 0));

    private static ReminderTextInput Input(
        string? template = null,
        string first = "Sami",
        string last = "Ben Ali",
        string clinic = "Cabinet El Menzah",
        params string[] acts) =>
        new(template, first, last, Appointment, acts, clinic);

    [Fact]
    public void The_Default_Wording_Names_The_Patient_The_Moment_And_The_Cabinet()
    {
        var rendered = ReminderText.Render(Input());

        Assert.Equal(
            "Rappel : Sami Ben Ali, vous avez un rendez-vous le 21/10/2026 à 14:30 chez Cabinet El Menzah.",
            rendered.Text);
        Assert.True(rendered.Fits);
    }

    [Fact]
    public void The_Acts_Are_Joined_In_Booking_Order()
    {
        var rendered = ReminderText.Render(Input("{patient} : {acte} le {date}.", acts: ["Détartrage", "Obturation"]));

        Assert.Equal("Sami Ben Ali : Détartrage + Obturation le 21/10/2026 à 14:30.", rendered.Text);
    }

    [Fact]
    public void Accents_Outside_Gsm7_Are_Replaced_So_The_Message_Stays_One_Sms()
    {
        var rendered = ReminderText.Render(Input("{patient}, contrôle prévu le {date} — « à jeun » ; ça ira.", first: "Hélène", last: "Benaïssa"));

        // é, è and à are GSM-7 and stay; ô, ï, ç, the dash and the guillemets are mapped.
        Assert.Equal("Hélène Benaissa, controle prévu le 21/10/2026 à 14:30 - \" à jeun \" ; ca ira.", rendered.Text);
    }

    [Fact]
    public void Gsm7_Extension_Characters_Which_Cost_Two_Are_Mapped()
    {
        var normalised = ReminderText.Normalise("Tarif [promo] ~ 10€ | ok\\");

        Assert.Equal("Tarif (promo) - 10EUR / ok/", normalised);
    }

    [Fact]
    public void A_Line_Break_Is_Kept_And_Counts_As_One()
    {
        var rendered = ReminderText.Render(Input("Rappel le {date}\r\nMerci de venir 10 min en avance."));

        Assert.Equal("Rappel le 21/10/2026 à 14:30\nMerci de venir 10 min en avance.", rendered.Text);
        Assert.DoesNotContain('\r', rendered.Text);
    }

    [Fact]
    public void Three_Empty_Lines_Become_One()
    {
        var rendered = ReminderText.Render(Input("Rappel le {date}\n\n\n\nA bientot"));

        Assert.Equal("Rappel le 21/10/2026 à 14:30\n\nA bientot", rendered.Text);
    }

    [Fact]
    public void WhatsApp_Receives_The_Lines_Joined_Because_Meta_Refuses_A_Newline()
    {
        var flattened = ReminderText.ForWhatsApp("Rappel le 21/10/2026 à 14:30\n\nMerci de venir en avance.");

        Assert.Equal("Rappel le 21/10/2026 à 14:30 · Merci de venir en avance.", flattened);
    }

    [Fact]
    public void Too_Many_Acts_Shrink_To_The_First_Plus_A_Count()
    {
        var acts = new[] { "Traitement endodontique molaire", "Reconstitution corono-radiculaire", "Détartrage complet" };
        var template = "Bonjour {patient}, RDV le {date} chez {clinic} pour : {acte}. Merci de prevenir en cas d'empechement.";

        var rendered = ReminderText.Render(Input(template, acts: acts));

        Assert.Contains("Traitement endodontique molaire +2", rendered.Text);
        Assert.True(rendered.Text.Length <= ReminderText.SmsMaxLength, rendered.Text);
    }

    [Fact]
    public void A_Long_Name_Shrinks_To_Its_Initial_Before_Anything_Else_Is_Cut()
    {
        var template = "Bonjour {patient}. Votre rendez-vous est le {date} chez {clinic}. Merci d'arriver 10 minutes en avance svp.";

        var rendered = ReminderText.Render(Input(
            template, first: "Mohamed Amine", last: "Ben Abdallah El Kefi", clinic: "Cabinet Dentaire du Lac 2"));

        Assert.Contains("Mohamed Amine B.", rendered.Text);
        Assert.Contains("21/10/2026 à 14:30", rendered.Text);
        Assert.Contains("Merci d'arriver 10 minutes en avance svp.", rendered.Text);
        Assert.True(rendered.Text.Length <= ReminderText.SmsMaxLength, rendered.Text);
    }

    [Fact]
    public void A_Patient_With_No_First_Name_Leaves_No_Stray_Punctuation()
    {
        var rendered = ReminderText.Render(Input("Bonjour {patient}, RDV le {date}.", first: "", last: ""));

        Assert.Equal("Bonjour, RDV le 21/10/2026 à 14:30.", rendered.Text);
    }

    [Fact]
    public void A_Name_Cannot_Inject_A_Line_Break_Or_A_Variable()
    {
        var rendered = ReminderText.Render(Input("{patient} le {date}", first: "Sami\n{clinic}", last: "Ali"));

        Assert.Equal("Sami (clinic) Ali le 21/10/2026 à 14:30", rendered.Text);
    }

    [Fact]
    public void Nothing_Ever_Exceeds_One_Sms()
    {
        var random = new Random(20261020);
        const string alphabet = "abcdefghijklmnopqrstuvwxyzéèàçôêABCDEFG ,.'-\n";
        string Word(int max) => new(Enumerable.Range(0, random.Next(0, max)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());

        for (var run = 0; run < 500; run++)
        {
            var template = $"{Word(60)} {{patient}} {Word(30)} {{date}} {Word(30)} {{acte}} {Word(20)} {{clinic}} {Word(40)}";
            var acts = Enumerable.Range(0, random.Next(0, 5)).Select(_ => Word(40)).ToArray();

            var rendered = ReminderText.Render(Input(template, Word(40), Word(40), Word(60), acts));

            Assert.True(rendered.Text.Length <= ReminderText.SmsMaxLength, $"{rendered.Text.Length}: {rendered.Text}");
        }
    }

    [Fact]
    public void A_Template_Accepted_At_Save_Never_Loses_The_Date_Or_The_Doctors_Text()
    {
        const string doctorsText = "Pensez a apporter vos radios.";
        var template = "{patient}, RDV le {date} chez {clinic} pour {acte}. " + doctorsText;
        const string clinic = "Cabinet Dentaire Ennasr";
        Assert.Null(ReminderTemplateRules.CheckTemplate(template, clinic));

        var rendered = ReminderText.Render(Input(
            template, first: "Mohamed Amine", last: "Ben Abdallah El Kefi Ben Youssef", clinic: clinic,
            acts: ["Traitement endodontique molaire", "Reconstitution corono-radiculaire"]));

        Assert.True(rendered.Fits);
        Assert.Contains("21/10/2026 à 14:30", rendered.Text);
        Assert.Contains(doctorsText, rendered.Text);
        Assert.Contains(clinic, rendered.Text);
    }

    [Fact]
    public void The_Moment_Is_Stated_In_Clinic_Local_Time()
    {
        Assert.Equal("21/10/2026 à 14:30", ReminderText.FormatMoment(Appointment));
        Assert.Equal(18, ReminderText.MomentLength);
    }

    [Fact]
    public void A_Recall_Sentence_Is_Held_To_The_Same_Alphabet_And_Limit()
    {
        var capped = ReminderText.Capped("Bonjour Sami, il est temps de programmer un contrôle " + new string('x', 200));

        Assert.Contains("controle", capped);
        Assert.Equal(ReminderText.SmsMaxLength, capped.Length);
    }
}
