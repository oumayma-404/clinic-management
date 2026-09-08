namespace ClinicManagement.Application.DTOs;

/// <summary>One recorded condition on a tooth (a patient's odontogram is a list of these).</summary>
public class ToothStateDto
{
    public Guid Id { get; set; }
    public int ToothNumber { get; set; }
    public string Condition { get; set; } = string.Empty;
    /// <summary>"Diagnosis" (charted) or "Treatment" (from a dental record).</summary>
    public string Source { get; set; } = string.Empty;
    public string? Surfaces { get; set; }
    public string? Note { get; set; }
    public DateTime TreatmentDate { get; set; }
    public Guid? DentalRecordId { get; set; }
    /// <summary>
    /// Which bridge this tooth belongs to, or null — see <c>ToothState.BridgeGroupId</c>. The client groups the
    /// travée on this; null means « ungrouped », which the chart answers with the legacy adjacency scan.
    /// </summary>
    public Guid? BridgeGroupId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Request to chart a diagnosis directly on the odontogram (before treatment).</summary>
public class DiagnoseToothInput
{
    public int ToothNumber { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string? Surfaces { get; set; }
    public string? Note { get; set; }
    /// <summary>
    /// The bridge this tooth belongs to, when a whole planned bridge is being charted in one gesture.
    ///
    /// <para>⚠️ <b>Client-supplied, and it has to be.</b> There is no bulk diagnose endpoint — the multi-tooth
    /// panel deliberately posts one tooth at a time so a partial failure can re-offer exactly the teeth that
    /// did not land — so the only place that knows « these five teeth are one bridge » is the gesture. It is an
    /// opaque grouping token, never a lookup key, and the server folds it away for any condition that is not a
    /// bridge unit. ⚠️ A retry must re-send the SAME id or the bridge splits in two.</para>
    /// </summary>
    public Guid? BridgeGroupId { get; set; }
}

/// <summary>One tooth's condition captured while adding/editing a dental record (feeds the odontogram).</summary>
public class ToothConditionInput
{
    public int ToothNumber { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string? Surfaces { get; set; }
    public string? Note { get; set; }
}
