namespace ClinicManagement.API.Models;

/// <summary>
/// Request body for the valeur-de-la-lettre-cle write on <c>api/dental-acts</c>. Kept separate from the MediatR
/// command so the public HTTP contract does not couple to the internal command shape (and the route-bound
/// <c>Id</c> is never accepted from the body).
/// </summary>
public class UpdateCnamLetterValueRequest
{
    public decimal Value { get; set; }

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
