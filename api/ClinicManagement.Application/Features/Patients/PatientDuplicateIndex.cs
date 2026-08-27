using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>How a candidate matched somebody the clinic already has — or an earlier row of the same CSV file.</summary>
public enum PatientDuplicateKind
{
    None,

    /// <summary>Same name and same date of birth. The strongest signal short of an identity document.</summary>
    NameAndBirthDate,

    /// <summary>
    /// Same name, and the candidate supplied no date of birth to disagree with. Weaker, and deliberately still
    /// flagged — see <see cref="PatientDuplicateIndex"/>.
    /// </summary>
    Name,

    /// <summary>Same phone number, normalised to <c>+216</c> E.164 on both sides.</summary>
    Phone,
}

/// <summary>
/// One match, or <see cref="None"/>. <see cref="PatientId"/> is null when the thing matched is not a stored patient
/// but another row of the file currently being imported.
/// </summary>
public sealed record PatientDuplicateMatch(PatientDuplicateKind Kind, Guid? PatientId, string? Label)
{
    public static readonly PatientDuplicateMatch NoMatch = new(PatientDuplicateKind.None, null, null);

    public bool Found => Kind != PatientDuplicateKind.None;
}

/// <summary>
/// The clinic's existing patients keyed for matching — <b>the single answer to « do we already have this person? »</b>,
/// shared by the CSV import's per-row planning and by the hand-typed <c>CreatePatientCommand</c>.
///
/// <para><b>Duplicate matching is deliberately eager, and that asymmetry is the design.</b> A false positive costs the
/// user one confirmation (« Créer quand même »); a false negative creates a permanent second file for one person —
/// this product has <b>no merge and no soft delete</b>, so their appointments, their money and their allergies are
/// split across two records for ever, and the only remedy is deleting one, which is refused as soon as anything is
/// attached to it. Hence <see cref="PatientDuplicateKind.Name"/>: two different people really can share a name, but
/// when the candidate carries no date of birth there is nothing to tell them apart, and asking is cheaper than being
/// wrong.</para>
///
/// <para><b>Why it lives here rather than inside the import.</b> It began as a private nested class of
/// <c>PatientImportPlanner</c>, so an operator importing a spreadsheet was warned about a duplicate while a
/// receptionist typing the same person into the « Nouveau patient » form was not — and the hand-typed path is how the
/// overwhelming majority of patients are created. Moving it (rather than copying it) is what keeps « what counts as
/// the same person » one answer: the same normalisation, the same three signals, the same French labels, whichever
/// door the patient comes in through. Same reasoning as <see cref="PatientFromRequest"/> one file over.</para>
///
/// <para>Names are folded through <see cref="SearchTerm.Normalize"/> — the solution's existing case-and-accent
/// authority — so « BEN SALAH » and « Ben Salah » are one person, and this cannot disagree with the patient search
/// about that. Phones are folded through <see cref="PhoneNumber.ToE164"/>, which is what makes phone matching
/// possible at all: the hand-typed write path stores the number as typed, so the same patient exists in the database
/// as « 20 123 456 » and arrives as « +216 20 12 34 56 ».</para>
/// </summary>
public sealed class PatientDuplicateIndex
{
    /// <summary>
    /// Machine-readable tag on the refusal, so a client can offer « Créer quand même » and retry with
    /// <c>AllowDuplicate</c> instead of treating it as a dead end — the same contract as
    /// <c>AppointmentScheduling.SlotTakenCode</c>.
    ///
    /// <para>A code rather than the French message, for the same reason: the message names the person matched and the
    /// reason they matched, and is reworded freely. Matching on its text would turn the confirmation into a hard
    /// block the first time somebody edits a sentence — and a hard block here would make a genuine second patient of
    /// the same name impossible to register.</para>
    /// </summary>
    public const string RefusalCode = "patient_duplicate";

    private readonly record struct Entry(Guid? PatientId, string Label, DateTime? DateOfBirth);

    private readonly Dictionary<string, List<Entry>> _byName = new();
    private readonly Dictionary<string, Entry> _byPhone = new();

    public static PatientDuplicateIndex Build(IReadOnlyList<PatientIdentity> patients)
    {
        var index = new PatientDuplicateIndex();
        foreach (var p in patients)
        {
            index.Add(
                p.LastName,
                p.FirstName,
                p.DateOfBirth,
                p.PhoneNumber,
                p.Id,
                $"{p.FirstName} {p.LastName}".Trim());
        }

        return index;
    }

    public void Add(
        string lastName,
        string firstName,
        DateTime? dateOfBirth,
        string? phoneNumber,
        Guid? patientId,
        string label)
    {
        var entry = new Entry(patientId, label, dateOfBirth?.Date);

        var nameKey = NameKey(lastName, firstName);
        if (nameKey.Length > 0)
        {
            if (!_byName.TryGetValue(nameKey, out var list))
            {
                list = new List<Entry>();
                _byName[nameKey] = list;
            }

            list.Add(entry);
        }

        var phoneKey = PhoneNumber.ToE164(phoneNumber);
        if (phoneKey != null)
        {
            // First writer wins: with two existing records already sharing a number, naming either of them is an
            // equally true answer to « this matches somebody you have ».
            _byPhone.TryAdd(phoneKey, entry);
        }
    }

    public PatientDuplicateMatch Match(
        string lastName,
        string firstName,
        DateTime? dateOfBirth,
        string? phoneNumber)
    {
        var nameKey = NameKey(lastName, firstName);
        if (nameKey.Length > 0 && _byName.TryGetValue(nameKey, out var namesakes))
        {
            // ⚠️ Null is « no date of birth supplied » and must never be compared as a real date — which is also why
            // the stored side is nullable now rather than a sentinel: an undated patient on file and an undated
            // candidate must not match each other *on the date*, they match on the name-alone rule below, exactly as
            // they did when the sentinel existed (D-2 — neither wider nor narrower).
            if (dateOfBirth is { } born)
            {
                var sameDay = namesakes.FirstOrDefault(e => e.DateOfBirth == born.Date);
                if (sameDay != default)
                {
                    return new PatientDuplicateMatch(
                        PatientDuplicateKind.NameAndBirthDate,
                        sameDay.PatientId,
                        Describe(sameDay, "même nom et date de naissance"));
                }
            }
            else
            {
                var first = namesakes[0];
                return new PatientDuplicateMatch(
                    PatientDuplicateKind.Name,
                    first.PatientId,
                    Describe(first, "même nom, aucune date de naissance pour distinguer"));
            }
        }

        var phoneKey = PhoneNumber.ToE164(phoneNumber);
        if (phoneKey != null && _byPhone.TryGetValue(phoneKey, out var samePhone))
        {
            return new PatientDuplicateMatch(
                PatientDuplicateKind.Phone,
                samePhone.PatientId,
                Describe(samePhone, "même téléphone"));
        }

        return PatientDuplicateMatch.NoMatch;
    }

    /// <summary>
    /// The French refusal shown when a hand-typed patient matches someone on file.
    ///
    /// <para>It names <b>who</b> was matched and <b>why</b> (the <see cref="Match"/> label), then states the one fact
    /// that makes the confirmation worth reading: a second file for the same person cannot be merged afterwards. The
    /// wording is here rather than in the handler so the import's per-row reason and this refusal cannot describe the
    /// same match differently.</para>
    /// </summary>
    public static string Refusal(PatientDuplicateMatch match) =>
        $"Ce patient existe déjà : {match.Label}. "
        + "Deux dossiers pour la même personne ne peuvent pas être fusionnés — ses rendez-vous, ses paiements et ses "
        + "allergies seraient séparés définitivement. Ouvrez le dossier existant, ou confirmez la création s'il "
        + "s'agit bien d'une autre personne.";

    private static string Describe(Entry entry, string reason) => $"{entry.Label} ({reason})";

    private static string NameKey(string lastName, string firstName)
    {
        var last = SearchTerm.Normalize(lastName);
        var first = SearchTerm.Normalize(firstName);
        return last.Length == 0 && first.Length == 0 ? string.Empty : $"{last}|{first}";
    }
}
