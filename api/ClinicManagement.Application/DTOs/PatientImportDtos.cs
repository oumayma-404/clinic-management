namespace ClinicManagement.Application.DTOs;

/// <summary>
/// What the import decided about one row. A closed set the client renders French labels for — the standing
/// English-token / French-label convention.
/// </summary>
public enum PatientImportRowOutcome
{
    /// <summary>Ready to create.</summary>
    Ready,

    /// <summary>
    /// Matches a patient the clinic already has (name + date of birth, or phone). <b>Skipped by default</b> — the
    /// spec's default, and the right one: this product has <b>no merge and no soft delete</b>
    /// (<c>Patient.cs:67-72</c>), so a duplicate created by mistake is a permanent second file for one person,
    /// splitting their appointments, their money and their allergies across two records.
    /// </summary>
    Duplicate,

    /// <summary>Cannot be created; <see cref="PatientImportRowDto.Errors"/> says why, in French.</summary>
    Invalid,

    /// <summary>Only in a commit result: the row was created.</summary>
    Created,

    /// <summary>Only in a commit result: the row was a duplicate the operator did not choose to create anyway.</summary>
    Skipped,

    /// <summary>
    /// Only in a commit result: the row passed the dry run and the write still refused it. Rare, and reported per
    /// row rather than as a failed import — see <c>ImportPatientsCommand</c>.
    /// </summary>
    Failed,
}

/// <summary>One row of the preview or of the result.</summary>
public class PatientImportRowDto
{
    /// <summary>The line in the uploaded file, header included — the number the operator sees in Excel's gutter.</summary>
    public int LineNumber { get; set; }

    /// <summary>« Ben Salah Amine », for a report that can be read without the file open beside it.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public PatientImportRowOutcome Outcome { get; set; }

    /// <summary>French reasons the row cannot be created.</summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// French notes about what will be dropped or defaulted if the row is created as-is (an incomplete address, an
    /// unreadable « Sexe »). Deliberately distinct from <see cref="Errors"/>: the row still imports.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>The existing patient this row matches, when <see cref="Outcome"/> is a duplicate.</summary>
    public Guid? DuplicateOfPatientId { get; set; }

    /// <summary>Whose record it matches, and on what — « Amine Ben Salah (même nom et date de naissance) ».</summary>
    public string? DuplicateOf { get; set; }
}

/// <summary>
/// The dry run: what an import <i>would</i> do, with the mapping it used.
/// </summary>
public class PatientImportPreviewDto
{
    /// <summary>The file's own column headings, in order — what the mapping screen offers per field.</summary>
    public List<string> Headers { get; set; } = new();

    /// <summary>
    /// The mapping actually applied, as <c>field token → column index</c>. Echoed back rather than assumed: the
    /// client sends nothing on the first upload and renders whatever detection found, so the two must agree on
    /// which columns were read.
    /// </summary>
    public Dictionary<string, int> Mapping { get; set; } = new();

    /// <summary>Every mappable field, its French label and whether it is required — so the UI needs no copy of the set.</summary>
    public List<PatientImportFieldDto> Fields { get; set; } = new();

    /// <summary>What the reader decided about the file, stated so a mis-read file is diagnosable.</summary>
    public string Delimiter { get; set; } = string.Empty;

    public string Encoding { get; set; } = string.Empty;

    /// <summary>
    /// True when the file held more rows than one import may carry. Surfaced, never silent: an import that stopped
    /// at row 5 000 of 8 000 while reporting success is a practice that believes it has migrated.
    /// </summary>
    public bool Truncated { get; set; }

    public List<PatientImportRowDto> Rows { get; set; } = new();

    public int ReadyCount { get; set; }

    public int DuplicateCount { get; set; }

    public int InvalidCount { get; set; }
}

/// <summary>One mappable patient field, as the mapping screen needs it.</summary>
public class PatientImportFieldDto
{
    /// <summary>The stable English token the client sends back in its mapping.</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>The French label to show.</summary>
    public string Label { get; set; } = string.Empty;

    public bool Required { get; set; }
}

/// <summary>What the commit did.</summary>
public class PatientImportResultDto
{
    public int CreatedCount { get; set; }

    public int SkippedCount { get; set; }

    public int FailedCount { get; set; }

    /// <summary>
    /// Every row and its outcome. The whole report, not only the failures: « 2 947 créés » is only believable
    /// beside the rows it did not create.
    /// </summary>
    public List<PatientImportRowDto> Rows { get; set; } = new();
}
