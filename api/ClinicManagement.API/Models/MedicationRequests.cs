namespace ClinicManagement.API.Models;

/// <summary>
/// Request bodies for the medication catalog admin endpoints. Kept separate from the MediatR commands so the
/// public HTTP contract does not couple to internal command shapes (and route-bound ids like <c>Id</c> are
/// never accepted from the body).
/// </summary>
public class CreateMedicationRequest
{
    public string BrandName { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    public string Strength { get; set; } = string.Empty;
    public List<string> Dcis { get; set; } = new();
}

public class UpdateMedicationRequest
{
    public string BrandName { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    public string Strength { get; set; } = string.Empty;
    public List<string> Dcis { get; set; } = new();

    /// <summary>
    /// The <c>Version</c> the client read, round-tripped so a stale save is a 409 rather than a silent overwrite.
    ///
    /// <para>⚠️ Its ABSENCE here was the whole defect. Both commands already had <c>Version</c> and both handlers
    /// already called <c>SetExpectedVersion</c> — but this request model had no such property, so the value the
    /// browser sent was dropped at the seam and the command received <c>0</c>, which means « not supplied » and
    /// skips the check. `/dental-acts` binds its command straight from the body and was therefore protected;
    /// these two were not. Measured: the version advanced, and a replay of the old one still returned 200.</para>
    /// </summary>
    public uint Version { get; set; }
}
