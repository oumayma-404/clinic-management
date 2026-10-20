using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClinicManagement.Application.Common;

namespace ClinicManagement.Application.Features.Messaging;

/// <summary>What a reminder is rendered from — read at send time from the live appointment, patient and clinic.</summary>
public sealed record ReminderTextInput(
    string? Template,
    string FirstName,
    string LastName,
    DateTime AppointmentUtc,
    IReadOnlyList<string> ActNames,
    string ClinicName);

/// <summary>The rendered text, and whether it fitted without the last-resort cut.</summary>
public sealed record RenderedReminder(string Text, bool Fits);

/// <summary>
/// <b>The one renderer of a patient reminder</b>: its variables, its date format, its GSM-7 alphabet and its length
/// budget. The dispatcher renders with it at send time, the scheduler renders a preview with it at enqueue, and the
/// settings screen's preview and save-time validation (<see cref="ReminderTemplateRules"/>) use the same arithmetic —
/// so what the doctor sees counted is what the patient is billed for.
///
/// <para><b>One SMS is <see cref="SmsMaxLength"/> characters at our provider, and a reminder never exceeds it.</b> A
/// single character outside the GSM-7 alphabet (ê, ç, ô, a curly quote) switches the whole message to UCS-2 and cuts
/// the limit to 70, so <see cref="Normalise"/> maps those to their plain letters and drops the GSM-7 <i>extension</i>
/// characters, which count double.</para>
///
/// <para><b>Shrink order when the values do not fit</b>: the acts (« Détartrage + Obturation » → « Détartrage +1 » →
/// cut), then the patient's name (« Sami Ben Ali » → « Sami B. » → « Sami » → cut), then the clinic's name. The date
/// and the doctor's own text are never cut — <see cref="ReminderTemplateRules"/> refuses, at save, a template whose
/// worst case does not fit.</para>
/// </summary>
public static class ReminderText
{
    /// <summary>One SMS at Tunisie SMS.</summary>
    public const int SmsMaxLength = 157;

    public const string PatientVariable = "{patient}";
    public const string DateVariable = "{date}";
    public const string ActeVariable = "{acte}";
    public const string ClinicVariable = "{clinic}";

    public static readonly IReadOnlyList<string> Variables =
        [PatientVariable, DateVariable, ActeVariable, ClinicVariable];

    /// <summary>The wording used when the cabinet has not written its own. No <c>{acte}</c>: an SMS shows on a lock screen.</summary>
    public const string DefaultTemplate =
        "Rappel : {patient}, vous avez un rendez-vous le {date} chez {clinic}.";

    /// <summary>Room kept for the patient's name when judging a template at save (« Sami B. » and a little more).</summary>
    public const int ReservedPatientLength = 10;

    /// <summary>Room kept for the acts when the template uses <c>{acte}</c>.</summary>
    public const int ReservedActeLength = 12;

    /// <summary>At most this many line breaks in a row — one empty line between paragraphs.</summary>
    public const int MaxConsecutiveLineBreaks = 2;

    private static readonly CultureInfo FrCulture = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly Regex VariableToken = new(@"\{[^{}\s]*\}", RegexOptions.Compiled);
    private static readonly Regex KnownVariable = new(
        string.Join("|", Variables.Select(Regex.Escape)), RegexOptions.Compiled);
    private static readonly Regex SpaceRun = new(@"[ ]{2,}", RegexOptions.Compiled);
    private static readonly Regex LineBreakRun = new(@"\n{3,}", RegexOptions.Compiled);

    /// <summary>
    /// The GSM 03.38 basic alphabet: each of these is one character of the SMS. Anything else is either mapped by
    /// <see cref="Substitutions"/> or dropped.
    /// </summary>
    private const string Gsm7Basic =
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡"
        + "ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";

    private static readonly HashSet<char> Gsm7 = [.. Gsm7Basic];

    /// <summary>
    /// French and typographic characters outside GSM-7, mapped to what a phone shows without switching alphabet.
    /// The GSM-7 <i>extension</i> characters (<c>[ ] { } ~ ^ \ | €</c>) are mapped too: each costs two characters.
    /// </summary>
    private static readonly Dictionary<char, string> Substitutions = new()
    {
        ['â'] = "a", ['á'] = "a", ['ã'] = "a", ['À'] = "A", ['Â'] = "A", ['Á'] = "A", ['Ã'] = "A",
        ['ê'] = "e", ['ë'] = "e", ['È'] = "E", ['Ê'] = "E", ['Ë'] = "E",
        ['î'] = "i", ['ï'] = "i", ['í'] = "i", ['Î'] = "I", ['Ï'] = "I", ['Í'] = "I", ['Ì'] = "I",
        ['ô'] = "o", ['ó'] = "o", ['õ'] = "o", ['Ô'] = "O", ['Ó'] = "O", ['Ò'] = "O", ['Õ'] = "O",
        ['û'] = "u", ['ú'] = "u", ['Û'] = "U", ['Ù'] = "U", ['Ú'] = "U",
        ['ç'] = "c", ['ÿ'] = "y", ['Ÿ'] = "Y",
        ['œ'] = "oe", ['Œ'] = "OE",
        ['’'] = "'", ['‘'] = "'", ['´'] = "'", ['`'] = "'",
        ['“'] = "\"", ['”'] = "\"", ['«'] = "\"", ['»'] = "\"",
        ['–'] = "-", ['—'] = "-", ['−'] = "-", ['…'] = "...",
        [' '] = " ", [' '] = " ", [' '] = " ", ['\t'] = " ",
        ['['] = "(", [']'] = ")", ['{'] = "(", ['}'] = ")",
        ['~'] = "-", ['^'] = "", ['\\'] = "/", ['|'] = "/", ['€'] = "EUR",
    };

    /// <summary>How every reminder states the appointment's moment: clinic-local (Tunisia is UTC+1), « 21/10/2026 à 14:30 ».</summary>
    public static string FormatMoment(DateTime appointmentUtc) =>
        ClinicClock.ToClinicLocal(appointmentUtc).ToString("dd/MM/yyyy 'à' HH:mm", FrCulture);

    /// <summary>The length of <see cref="FormatMoment"/>'s output, which is fixed.</summary>
    public static readonly int MomentLength = FormatMoment(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)).Length;

    /// <summary>
    /// Maps <paramref name="text"/> onto the GSM-7 basic alphabet and tidies its whitespace: CRLF/CR become one LF,
    /// at most <see cref="MaxConsecutiveLineBreaks"/> in a row, runs of spaces collapse, each line and the whole text
    /// are trimmed. Known variable tokens are kept intact.
    /// </summary>
    public static string Normalise(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var cursor = 0;
        foreach (Match token in KnownVariable.Matches(text))
        {
            builder.Append(MapToGsm7(text[cursor..token.Index]));
            builder.Append(token.Value);
            cursor = token.Index + token.Length;
        }

        builder.Append(MapToGsm7(text[cursor..]));
        return TidyWhitespace(builder.ToString());
    }

    /// <summary>The variable-looking tokens of <paramref name="template"/> that are not one of <see cref="Variables"/>.</summary>
    public static IReadOnlyList<string> UnknownVariables(string? template) =>
        string.IsNullOrEmpty(template)
            ? []
            : VariableToken.Matches(template)
                .Select(m => m.Value)
                .Where(token => !Variables.Contains(token))
                .Distinct()
                .ToList();

    /// <summary>
    /// The longest this template can render to for this clinic: its own text, the date, the clinic's name, and the
    /// reserved room for the patient and the acts. The figure <see cref="ReminderTemplateRules"/> holds to the limit.
    /// </summary>
    public static int WorstCaseLength(string? template, string clinicName)
    {
        var shape = Shape.Of(EffectiveTemplate(template), NormaliseValue(clinicName), moment: null);
        return shape.Length(ReservedPatientLength, ReservedActeLength, shape.Clinic.Length);
    }

    /// <summary>Renders the reminder, shrinking the values until it fits in <paramref name="maxLength"/>.</summary>
    public static RenderedReminder Render(ReminderTextInput input, int maxLength = SmsMaxLength)
    {
        var shape = Shape.Of(
            EffectiveTemplate(input.Template), NormaliseValue(input.ClinicName), FormatMoment(input.AppointmentUtc));
        var patientForms = PatientForms(NormaliseValue(input.FirstName), NormaliseValue(input.LastName));
        var acteForms = ActeForms(input.ActNames.Select(NormaliseValue).Where(a => a.Length > 0).Distinct().ToList());

        var patient = patientForms[0];
        var clinic = shape.Clinic;
        bool Fits(string p, string a, string c) => shape.Compose(p, a, c).Length <= maxLength;
        RenderedReminder Rendered(string p, string a, string c) => new(shape.Compose(p, a, c), true);

        // 1. The acts, structurally: every name, then the first one « +N ».
        foreach (var form in acteForms)
        {
            if (Fits(patient, form, clinic))
            {
                return Rendered(patient, form, clinic);
            }
        }

        // 2. The acts, cut — not below their reserved room.
        var acte = Cut(acteForms[^1], Math.Max(
            ReservedActeLength, Room(shape.Acte, shape.Length(patient.Length, 0, clinic.Length), maxLength)));
        if (Fits(patient, acte, clinic))
        {
            return Rendered(patient, acte, clinic);
        }

        // 3. The patient's name, structurally, then cut — not below its reserved room.
        foreach (var form in patientForms.Skip(1))
        {
            if (Fits(form, acte, clinic))
            {
                return Rendered(form, acte, clinic);
            }
        }

        patient = Cut(patientForms[^1], Math.Max(
            ReservedPatientLength, Room(shape.Patient, shape.Length(0, acte.Length, clinic.Length), maxLength)));
        if (Fits(patient, acte, clinic))
        {
            return Rendered(patient, acte, clinic);
        }

        // 4. The clinic's name — reachable only when it was renamed after the template was accepted.
        clinic = Cut(clinic, Room(shape.ClinicCount, shape.Length(patient.Length, acte.Length, 0), maxLength));
        if (Fits(patient, acte, clinic))
        {
            return Rendered(patient, acte, clinic);
        }

        // 5. The reserves themselves: the acts, then the name.
        acte = Cut(acte, Room(shape.Acte, shape.Length(patient.Length, 0, clinic.Length), maxLength));
        if (Fits(patient, acte, clinic))
        {
            return Rendered(patient, acte, clinic);
        }

        patient = Cut(patient, Room(shape.Patient, shape.Length(0, acte.Length, clinic.Length), maxLength));
        if (Fits(patient, acte, clinic))
        {
            return Rendered(patient, acte, clinic);
        }

        // 6. A template saved before the rule existed: cut hard. Never more than one SMS.
        var text = shape.Compose(patient, acte, clinic);
        return new RenderedReminder(text[..maxLength].TrimEnd(), false);
    }

    /// <summary>
    /// A text that is not built from a template (the recall's fixed sentence) held to the same alphabet and the same
    /// one-SMS limit.
    /// </summary>
    public static string Capped(string text, int maxLength = SmsMaxLength)
    {
        var normalised = Normalise(text);
        return normalised.Length <= maxLength ? normalised : normalised[..maxLength].TrimEnd();
    }

    /// <summary>
    /// WhatsApp refuses a template parameter carrying a line break (error 132018), so the same text reaches it with
    /// its lines joined by « · ».
    /// </summary>
    public static string ForWhatsApp(string text) =>
        string.Join(" · ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string EffectiveTemplate(string? template) =>
        Normalise(string.IsNullOrWhiteSpace(template) ? DefaultTemplate : template);

    /// <summary>A value never carries a line break or a variable token, so a name cannot inject either.</summary>
    private static string NormaliseValue(string? value) =>
        TidyWhitespace(MapToGsm7(value ?? string.Empty).Replace('\n', ' ').Replace('\r', ' '));

    private static IReadOnlyList<string> PatientForms(string first, string last)
    {
        if (first.Length == 0)
        {
            return [last];
        }

        if (last.Length == 0)
        {
            return [first];
        }

        return new[] { $"{first} {last}", $"{first} {last[0]}.", first }.Distinct().ToList();
    }

    private static IReadOnlyList<string> ActeForms(IReadOnlyList<string> acts) => acts.Count switch
    {
        0 => [string.Empty],
        1 => [acts[0]],
        _ => [string.Join(" + ", acts), $"{acts[0]} +{acts.Count - 1}"],
    };

    /// <summary>How long each occurrence of a variable may be, given the rest of the text's length.</summary>
    private static int Room(int occurrences, int lengthWithoutIt, int maxLength) =>
        occurrences == 0 ? int.MaxValue : Math.Max(0, (maxLength - lengthWithoutIt) / occurrences);

    /// <summary>Cuts <paramref name="value"/> to <paramref name="length"/>, marking the cut with « . ».</summary>
    private static string Cut(string value, int length)
    {
        if (value.Length <= length)
        {
            return value;
        }

        if (length <= 1)
        {
            return string.Empty;
        }

        return value[..(length - 1)].TrimEnd() + ".";
    }

    private static string MapToGsm7(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (Substitutions.TryGetValue(c, out var replacement))
            {
                builder.Append(replacement);
            }
            else if (Gsm7.Contains(c))
            {
                builder.Append(c);
            }

            // Anything else (Arabic script, an emoji) has no GSM-7 form and is dropped rather than doubling the cost.
        }

        return builder.ToString();
    }

    private static string TidyWhitespace(string text)
    {
        var unified = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = unified.Split('\n').Select(line => SpaceRun.Replace(line, " ").Trim());
        return LineBreakRun.Replace(string.Join('\n', lines), new string('\n', MaxConsecutiveLineBreaks)).Trim();
    }

    /// <summary>
    /// A normalised template with <c>{date}</c> already substituted, and how many times each other variable occurs —
    /// enough to know a rendering's exact length before composing it.
    /// </summary>
    private sealed record Shape(string Template, int FixedLength, int Patient, int Acte, int ClinicCount, string Clinic)
    {
        /// <param name="moment">The stated moment, or null to measure with a placeholder of its fixed length.</param>
        public static Shape Of(string normalisedTemplate, string clinic, string? moment)
        {
            var dated = normalisedTemplate.Replace(DateVariable, moment ?? new string('#', MomentLength));
            var patient = Occurrences(dated, PatientVariable);
            var acte = Occurrences(dated, ActeVariable);
            var clinicCount = Occurrences(dated, ClinicVariable);
            var fixedLength = dated.Length
                - patient * PatientVariable.Length
                - acte * ActeVariable.Length
                - clinicCount * ClinicVariable.Length;
            return new Shape(dated, fixedLength, patient, acte, clinicCount, clinic);
        }

        public int Length(int patientLength, int acteLength, int clinicLength) =>
            FixedLength + Patient * patientLength + Acte * acteLength + ClinicCount * clinicLength;

        /// <summary>
        /// Substitutes the values; an empty value leaves no « , , » or double space behind (a patient with no
        /// first name, a séance with no act).
        /// </summary>
        public string Compose(string patient, string acte, string clinic)
        {
            var text = Template
                .Replace(PatientVariable, patient)
                .Replace(ActeVariable, acte)
                .Replace(ClinicVariable, clinic);
            return Tidy(text);
        }

        private static int Occurrences(string text, string token)
        {
            var count = 0;
            for (var index = text.IndexOf(token, StringComparison.Ordinal);
                 index >= 0;
                 index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }
    }

    private static string Tidy(string text)
    {
        var tidied = TidyWhitespace(text);
        return tidied.Replace(" ,", ",").Replace(",,", ",").Replace(", .", ".");
    }
}
