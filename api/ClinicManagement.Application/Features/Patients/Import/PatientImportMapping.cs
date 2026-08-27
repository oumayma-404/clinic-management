using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Features.Patients.Import;

/// <summary>
/// The plan → the wire. One mapper for both endpoints, so the preview's rows and the result's rows are the same
/// shape — the report after an import is read against the preview taken before it, and two row shapes would make
/// that comparison a translation exercise.
/// </summary>
public static class PatientImportMapping
{
    public static PatientImportPreviewDto ToPreview(PatientImportPlan plan)
    {
        var rows = plan.Rows.Select(ToRowDto).ToList();

        return new PatientImportPreviewDto
        {
            Headers = plan.Document.Headers.ToList(),
            Mapping = plan.Mapping.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            Fields = PatientImportFields.All.Select(f => new PatientImportFieldDto
            {
                Field = f.ToString(),
                Label = PatientImportFields.Label(f),
                Required = PatientImportFields.IsRequired(f),
            }).ToList(),
            // Named rather than emitted raw: a tab is invisible in a UI, and « le fichier a été lu comme séparé par
            // des tabulations » is what tells an operator why every row landed in one column.
            Delimiter = DelimiterLabel(plan.Document.Delimiter),
            Encoding = plan.Document.Encoding,
            Truncated = plan.Document.Truncated,
            Rows = rows,
            ReadyCount = rows.Count(r => r.Outcome == PatientImportRowOutcome.Ready),
            DuplicateCount = rows.Count(r => r.Outcome == PatientImportRowOutcome.Duplicate),
            InvalidCount = rows.Count(r => r.Outcome == PatientImportRowOutcome.Invalid),
        };
    }

    public static PatientImportRowDto ToRowDto(PlannedImportRow row) => new()
    {
        LineNumber = row.Row.LineNumber,
        DisplayName = row.DisplayName,
        // ⚠️ Invalid outranks duplicate. A row that cannot be created is not a decision the operator has to make
        // about an existing patient, and offering « Créer quand même » on a row with an unreadable date of birth
        // would offer an action that is guaranteed to fail.
        Outcome = row.IsInvalid
            ? PatientImportRowOutcome.Invalid
            : row.IsDuplicate
                ? PatientImportRowOutcome.Duplicate
                : PatientImportRowOutcome.Ready,
        Errors = row.Read.Errors.ToList(),
        Warnings = row.Read.Warnings.ToList(),
        DuplicateOfPatientId = row.DuplicateOfPatientId,
        DuplicateOf = row.DuplicateOfLabel,
    };

    private static string DelimiterLabel(char delimiter) => delimiter switch
    {
        ';' => "point-virgule (;)",
        ',' => "virgule (,)",
        '\t' => "tabulation",
        _ => delimiter.ToString(),
    };
}
