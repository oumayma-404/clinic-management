using Microsoft.AspNetCore.Http;

namespace ClinicManagement.API.Models;

/// <summary>
/// The multipart body of the two patient-import endpoints (L5).
///
/// <para>⚠️ <b><see cref="Mapping"/> is a JSON string, not a bound dictionary.</b> A multipart form has no way to
/// carry a nested object, and the usual workaround — one field per key (<c>mapping[LastName]=0</c>) — binds
/// silently and partially: a typo in a key becomes a field that is simply not mapped, which is a column of 3 000
/// blank telephones rather than an error. One JSON value either parses or is refused with a French reason.</para>
/// </summary>
public class PatientImportRequest
{
    public IFormFile File { get; set; } = null!;

    /// <summary>
    /// <c>{"LastName":0,"FirstName":1,…}</c> — field token → 0-based column index, a negative index meaning « do not
    /// import this field ». Omit it entirely on the first upload to let the server detect the mapping from the
    /// headers.
    /// </summary>
    public string? Mapping { get; set; }

    /// <summary>
    /// Comma-separated file lines the operator chose to create despite a duplicate match. A string rather than a
    /// bound <c>List&lt;int&gt;</c> for the reason above, and because the list can be long.
    /// </summary>
    public string? CreateAnywayLines { get; set; }
}
