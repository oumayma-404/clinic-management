using ClinicManagement.Application.Common.Csv;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Import;

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
/// <para><b>Duplicate matching itself is not here</b> — it is <see cref="PatientDuplicateIndex"/>, shared with the
/// hand-typed create path, which is where the eagerness and the three signals are explained. It used to be a private
/// nested class of this one, which meant an imported row was checked while a receptionist typing the same person into
/// the patient form was not.</para>
///
/// <para>What stays here is that rows are also matched against <b>each other</b>. A spreadsheet listing the same
/// patient twice is at least as common as one re-listing a patient the clinic already has, and no per-row database
/// query could see it.</para>
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
        var index = PatientDuplicateIndex.Build(existingPatients);
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
}

