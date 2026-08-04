using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Csv;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Patients.Import;

/// <summary>How a row matched something that already exists — or an earlier row of the same file.</summary>
public enum PatientDuplicateKind
{
    None,

    /// <summary>Same name and same date of birth. The strongest signal short of an identity document.</summary>
    NameAndBirthDate,

    /// <summary>
    /// Same name, and the arriving row supplied no date of birth to disagree with. Weaker, and deliberately still
    /// flagged — see <see cref="PatientImportPlanner"/>.
    /// </summary>
    Name,

    /// <summary>Same phone number, normalised to <c>+216</c> E.164 on both sides.</summary>
    Phone,
}

/// <summary>One row, read and matched.</summary>
public sealed record PlannedImportRow(
    CsvRow Row,
    PatientImportRowRead Read,
    PatientDuplicateKind DuplicateKind,
    Guid? DuplicateOfPatientId,
    string? DuplicateOfLabel,
    string DisplayName)
{
    public bool IsInvalid => Read.Errors.Count > 0;

    public bool IsDuplicate => DuplicateKind != PatientDuplicateKind.None;
}

/// <summary>The whole file, read and matched, plus the mapping that was applied.</summary>
public sealed record PatientImportPlan(
    CsvDocument Document,
    IReadOnlyDictionary<PatientImportField, int> Mapping,
    IReadOnlyList<PlannedImportRow> Rows);

/// <summary>
/// Reads an uploaded file into a decision per row (L5, import half) — <b>the one implementation the dry run and the
/// commit both use</b>.
///
/// <para>That sharing is the entire value of the dry run. A preview built by different code from the commit is a
/// promise the commit does not have to keep, and the spec's requirement (« a <b>dry-run preview</b> with per-row
/// validation ») is only meaningful if « what the preview said » and « what the import did » cannot differ. So this
/// class is pure: bytes, a mapping and the clinic's existing identities in, decisions out. No repository, no
/// <c>DbContext</c>, no clock beyond <c>ClinicClock</c>.</para>
///
/// <para><b>Duplicate matching is deliberately eager, and that asymmetry is the design.</b> A false positive costs
/// the operator one checkbox (« Créer quand même »); a false negative creates a permanent second file for one
/// person — this product has <b>no merge and no soft delete</b>, so their appointments, their money and their
/// allergies are split across two records for ever, and the only remedy is deleting one, which is refused as soon
/// as anything is attached to it. Hence <see cref="PatientDuplicateKind.Name"/>: two different people really can
/// share a name, but when the arriving row carries no date of birth there is nothing to tell them apart, and asking
/// is cheaper than being wrong.</para>
///
/// <para>Rows are also matched against <b>each other</b>. A spreadsheet listing the same patient twice is at least
/// as common as one re-listing a patient the clinic already has, and no per-row database query could see it.</para>
/// </summary>
public static class PatientImportPlanner
{
    public static Result<PatientImportPlan> Build(
        byte[] fileContent,
        IReadOnlyDictionary<string, int>? requestedMapping,
        IReadOnlyList<PatientIdentity> existingPatients)
    {
        CsvDocument document;
        try
        {
            document = CsvReader.Read(fileContent);
        }
        catch (InvalidOperationException ex)
        {
            // The reader's own French message ( « Le fichier est vide. » … ). A malformed file is a refusal of the
            // whole request, unlike a malformed row — there is nothing to preview.
            return Result<PatientImportPlan>.Failure(ex.Message);
        }

        var mappingResult = ResolveMapping(document, requestedMapping);
        if (mappingResult.IsFailure)
        {
            return Result<PatientImportPlan>.FailureFrom(mappingResult);
        }

        var mapping = mappingResult.Value!;
        var index = ExistingIndex.Build(existingPatients);
        var rows = new List<PlannedImportRow>(document.Rows.Count);

        foreach (var row in document.Rows)
        {
            var read = PatientImportRowReader.Read(row, mapping);
            var displayName = DisplayName(row, mapping);

            if (read.Command == null)
            {
                rows.Add(new PlannedImportRow(
                    row, read, PatientDuplicateKind.None, null, null, displayName));
                continue;
            }

            var match = index.Match(read.Command.LastName, read.Command.FirstName, read.Command.DateOfBirth, read.Command.PhoneNumber);

            rows.Add(new PlannedImportRow(
                row,
                read,
                match.Kind,
                match.PatientId,
                match.Label,
                displayName));

            // A row that will be created becomes part of what later rows are matched against — including a row the
            // operator may yet choose to skip. Erring that way is right for the same reason the matching is eager:
            // if the file lists somebody three times, the second and third must both be flagged, whatever is
            // decided about the first.
            index.Add(
                read.Command.LastName,
                read.Command.FirstName,
                read.Command.DateOfBirth,
                read.Command.PhoneNumber,
                patientId: null,
                label: $"ligne {row.LineNumber} du fichier");
        }

        return Result<PatientImportPlan>.Success(new PatientImportPlan(document, mapping, rows));
    }

    /// <summary>
    /// The mapping to apply: what the client asked for, else auto-detection over the headers.
    ///
    /// <para>An out-of-range column index is <b>refused</b> rather than ignored: it means the client is mapping
    /// against different headers from the ones in this file (a re-upload of the wrong file after building a
    /// mapping), and silently reading « column 12 » as blank would import 3 000 patients with no telephone.</para>
    /// </summary>
    private static Result<Dictionary<PatientImportField, int>> ResolveMapping(
        CsvDocument document,
        IReadOnlyDictionary<string, int>? requested)
    {
        Dictionary<PatientImportField, int> mapping;

        if (requested is { Count: > 0 })
        {
            mapping = new Dictionary<PatientImportField, int>();
            foreach (var (token, columnIndex) in requested)
            {
                if (!Enum.TryParse<PatientImportField>(token, ignoreCase: true, out var field))
                {
                    return Result<Dictionary<PatientImportField, int>>.Failure(
                        $"Champ inconnu dans la correspondance des colonnes : « {token} ».");
                }

                // A negative index is the client's own « ne pas importer » — a field deliberately left unmapped.
                if (columnIndex < 0)
                {
                    continue;
                }

                if (columnIndex >= document.Headers.Count)
                {
                    return Result<Dictionary<PatientImportField, int>>.Failure(
                        $"La colonne n° {columnIndex + 1} associée à « {PatientImportFields.Label(field)} » "
                        + $"n'existe pas dans ce fichier ({document.Headers.Count} colonnes).");
                }

                mapping[field] = columnIndex;
            }
        }
        else
        {
            mapping = PatientImportFields.Detect(document.Headers);
        }

        var missing = PatientImportFields.Required.Where(f => !mapping.ContainsKey(f)).ToList();
        if (missing.Count > 0)
        {
            // Refused for the file, not per row: without a name column every single row would be invalid, and 3 000
            // identical row errors is a worse way to say « you have not mapped the Nom column ».
            return Result<Dictionary<PatientImportField, int>>.Failure(
                "Colonnes obligatoires non associées : "
                + string.Join(", ", missing.Select(PatientImportFields.Label))
                + ". Choisissez la colonne du fichier qui correspond à chacune.");
        }

        return Result<Dictionary<PatientImportField, int>>.Success(mapping);
    }

    /// <summary>
    /// « Ben Salah Amine », taken from the <b>raw cells</b> rather than from the parsed command, so an invalid row
    /// still has something to be listed under. A report of « ligne 47 : nom vide » with no name is only actionable
    /// with the file open beside it.
    /// </summary>
    private static string DisplayName(CsvRow row, IReadOnlyDictionary<PatientImportField, int> mapping)
    {
        string Cell(PatientImportField field) =>
            mapping.TryGetValue(field, out var i) ? row.Cell(i).Trim() : string.Empty;

        var name = string.Join(' ', new[] { Cell(PatientImportField.LastName), Cell(PatientImportField.FirstName) }
            .Where(p => p.Length > 0));

        return name.Length > 0 ? name : $"(ligne {row.LineNumber})";
    }

    /// <summary>
    /// The clinic's existing patients keyed for matching, plus whatever the current file has already planned.
    ///
    /// <para>Names are folded through <see cref="SearchTerm.Normalize"/> — the solution's existing
    /// case-and-accent authority — so « BEN SALAH » and « Ben Salah » are one person, and the import cannot
    /// disagree with the patient search about that. Phones are folded through <see cref="PhoneNumber.ToE164"/>,
    /// which is what makes matching possible at all: the hand-typed write path stores the number as typed, so the
    /// same patient exists in the database as « 20 123 456 » and arrives in the file as « +216 20 12 34 56 ».</para>
    /// </summary>
    private sealed class ExistingIndex
    {
        private readonly record struct Entry(Guid? PatientId, string Label, DateTime DateOfBirth);

        private readonly Dictionary<string, List<Entry>> _byName = new();
        private readonly Dictionary<string, Entry> _byPhone = new();

        public static ExistingIndex Build(IReadOnlyList<PatientIdentity> patients)
        {
            var index = new ExistingIndex();
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
            DateTime dateOfBirth,
            string? phoneNumber,
            Guid? patientId,
            string label)
        {
            var entry = new Entry(patientId, label, dateOfBirth.Date);

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
                // First writer wins: with two existing records already sharing a number, naming either of them is
                // an equally true answer to « this row matches somebody you have ».
                _byPhone.TryAdd(phoneKey, entry);
            }
        }

        public (PatientDuplicateKind Kind, Guid? PatientId, string? Label) Match(
            string lastName,
            string firstName,
            DateTime dateOfBirth,
            string? phoneNumber)
        {
            var nameKey = NameKey(lastName, firstName);
            if (nameKey.Length > 0 && _byName.TryGetValue(nameKey, out var namesakes))
            {
                // `default` is what the row reader passes for « no date of birth supplied », and it must never be
                // compared as a real date: the command replaces it with « 30 years ago », so comparing it would
                // match anyone whose stored date happens to be that day.
                var suppliedDob = dateOfBirth != default;

                if (suppliedDob)
                {
                    var sameDay = namesakes.FirstOrDefault(e => e.DateOfBirth == dateOfBirth.Date);
                    if (sameDay != default)
                    {
                        return (PatientDuplicateKind.NameAndBirthDate, sameDay.PatientId, Describe(sameDay, "même nom et date de naissance"));
                    }
                }
                else
                {
                    var first = namesakes[0];
                    return (PatientDuplicateKind.Name, first.PatientId, Describe(first, "même nom, aucune date de naissance pour distinguer"));
                }
            }

            var phoneKey = PhoneNumber.ToE164(phoneNumber);
            if (phoneKey != null && _byPhone.TryGetValue(phoneKey, out var samePhone))
            {
                return (PatientDuplicateKind.Phone, samePhone.PatientId, Describe(samePhone, "même téléphone"));
            }

            return (PatientDuplicateKind.None, null, null);
        }

        private static string Describe(Entry entry, string reason) => $"{entry.Label} ({reason})";

        private static string NameKey(string lastName, string firstName)
        {
            var last = SearchTerm.Normalize(lastName);
            var first = SearchTerm.Normalize(firstName);
            return last.Length == 0 && first.Length == 0 ? string.Empty : $"{last}|{first}";
        }
    }
}
