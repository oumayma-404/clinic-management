using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Domain.Common;
using ClinicManagement.API.Models;
using ClinicManagement.Application.Common.Csv;
using ClinicManagement.Application.Common.Files;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class PatientsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PatientsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get patients for the current user's clinic, optionally filtered by a search term (name / phone),
    /// by registration date, and capped at <paramref name="limit"/>.
    /// </summary>
    /// <param name="createdFrom">Inclusive lower bound on the registration date — backs the dashboard's
    /// « Nouveaux patients » drill-through, which must list exactly the patients that KPI counted.</param>
    /// <param name="createdTo">Inclusive upper bound on the registration date.</param>
    /// <param name="page">1-based page number. Omit — along with <paramref name="pageSize"/> — to get every match.</param>
    /// <param name="pageSize">
    /// Rows per page, clamped to <c>PageRequest.MaxPageSize</c>. Supplying either paging parameter switches the
    /// response to a page; supplying neither keeps the full set, which the patient pickers depend on.
    /// <para><b>The search spans the clinic, not the page.</b> <paramref name="searchTerm"/> is applied before
    /// the window in SQL, so a name on page 7 is found from page 1.</para>
    /// </param>

    /// <summary>
    /// « Exporter » (L5) — the same list, as a CSV.
    ///
    /// <para>⚠️ It re-sends the <b>identical query with no paging</b>, which the paging primitive models as a
    /// first-class case rather than as a huge page. That is what makes « honours the current filters, exports the
    /// whole filtered set, never the current page » true by construction instead of by discipline — the export
    /// cannot see a page to accidentally export.</para>
    /// </summary>
    [HttpGet("export")]
    public async Task<ActionResult> ExportPatients(
        [FromQuery] string? searchTerm = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        [FromQuery] bool flaggedOnly = false)
    {
        var result = await _mediator.Send(new GetPatientsQuery
        {
            SearchTerm = searchTerm,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo,
            FlaggedOnly = flaggedOnly,
        });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Csv(ExportTables.Patients(result.Value!.Items), "patients");
    }

    /// <summary>
    /// « Importer » (L5) — the <b>dry run</b>. Reads the uploaded CSV, applies the mapping (auto-detected when none
    /// is sent), and reports per row what an import would do. <b>Writes nothing.</b>
    ///
    /// <para>POST because the input is a file; a query behind it, so it emits no realtime broadcast — the same shape
    /// as the batch CNAM estimate. Re-send it on every mapping change: the file is not staged server-side.</para>
    /// </summary>
    /// <remarks>
    /// <b>AdminOrDoctor.</b> An import creates patient records in bulk, which is the clinical spine of the product
    /// and not something reception does — and per the API contract the two import endpoints share the export's gate.
    /// </remarks>
    [HttpPost("import/preview")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    [RequestSizeLimit(MaxImportFileBytes)]
    public async Task<ActionResult<PatientImportPreviewDto>> PreviewPatientImport(
        [FromForm] Models.PatientImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var content = await ReadImportFileAsync(request, cancellationToken);
        if (content.Error != null)
        {
            return Failure(content.Error);
        }

        var mapping = ParseMapping(request.Mapping);
        if (mapping.Error != null)
        {
            return Failure(mapping.Error);
        }

        var result = await _mediator.Send(
            new PreviewPatientImportQuery { FileContent = content.Bytes!, Mapping = mapping.Value },
            cancellationToken);

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// « Importer » (L5) — the <b>commit</b>. Creates every ready row, skips duplicates the operator did not tick,
    /// and returns the outcome of every line.
    ///
    /// <para>⚠️ The same file and the same mapping the preview was run with must be sent: nothing is staged between
    /// the two calls, so the commit re-reads and re-matches from scratch. That is what makes the preview honest —
    /// both calls run the identical planner — and it is why <c>createAnywayLines</c> is keyed on the <b>file line</b>
    /// rather than on a server-side row id that would not survive the round trip.</para>
    /// </summary>
    [HttpPost("import")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    [RequestSizeLimit(MaxImportFileBytes)]
    public async Task<ActionResult<PatientImportResultDto>> ImportPatients(
        [FromForm] Models.PatientImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var content = await ReadImportFileAsync(request, cancellationToken);
        if (content.Error != null)
        {
            return Failure(content.Error);
        }

        var mapping = ParseMapping(request.Mapping);
        if (mapping.Error != null)
        {
            return Failure(mapping.Error);
        }

        var result = await _mediator.Send(
            new ImportPatientsCommand
            {
                FileContent = content.Bytes!,
                Mapping = mapping.Value,
                CreateAnywayLines = ParseLines(request.CreateAnywayLines),
            },
            cancellationToken);

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// 8 MB. A CSV of 5 000 patients with every column filled is well under 2 MB, so this is generous for the real
    /// case while keeping the whole file in memory a bounded decision rather than an open one.
    /// </summary>
    private const int MaxImportFileBytes = 8 * 1024 * 1024;

    /// <summary>
    /// The uploaded bytes, or a French reason. Buffered whole rather than streamed: the reader has to see the header
    /// row and the data together to detect the delimiter and the encoding, and a CSV small enough to be an import is
    /// small enough to hold.
    /// </summary>
    private static async Task<(byte[]? Bytes, string? Error)> ReadImportFileAsync(
        Models.PatientImportRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File == null || request.File.Length == 0)
        {
            return (null, "Aucun fichier reçu. Choisissez un fichier CSV.");
        }

        if (request.File.Length > MaxImportFileBytes)
        {
            return (null, "Fichier trop volumineux (maximum 8 Mo).");
        }

        // The import reads the bytes into memory by design (the planner is pure over the whole file), but what
        // may be sent is the catalog's decision like every other door — a .exe named .csv is refused here too.
        var validation = await FileUploadValidator.ValidateAsync(
            FileUploadProfile.Csv,
            request.File.FileName,
            request.File.Length,
            request.File.OpenReadStream(),
            cancellationToken);

        if (validation.IsFailure)
        {
            return (null, validation.Error);
        }

        using var buffer = new MemoryStream();
        await validation.Value!.Content.CopyToAsync(buffer, cancellationToken);
        return (buffer.ToArray(), null);
    }

    /// <summary>
    /// The mapping JSON → a dictionary, or a French reason. A malformed value is <b>refused</b> rather than treated
    /// as « detect it yourself »: silently re-detecting would discard the mapping the operator just built and import
    /// against a different one than the preview showed them.
    /// </summary>
    private static (Dictionary<string, int>? Value, string? Error) ParseMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, null);
        }

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            return (parsed, null);
        }
        catch (System.Text.Json.JsonException)
        {
            return (null, "La correspondance des colonnes est illisible. Recommencez l'import.");
        }
    }

    /// <summary>
    /// « 12,47,203 » → the line numbers. An unparseable entry is dropped, not refused: this list only ever
    /// <i>widens</i> what gets created, so the safe failure is to skip that duplicate — the default the spec asks for.
    /// </summary>
    private static List<int> ParseLines(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<int>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => int.TryParse(part, out var line) ? line : -1)
                .Where(line => line > 0)
                .Distinct()
                .ToList();

    [HttpGet]
    public async Task<ActionResult<PagedResult<PatientDto>>> GetPatients(
        [FromQuery] string? searchTerm = null,
        [FromQuery] int? limit = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] bool flaggedOnly = false)
    {
        var query = new GetPatientsQuery
        {
            SearchTerm = searchTerm,
            Limit = limit,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo,
            Page = page,
            PageSize = pageSize,
            FlaggedOnly = flaggedOnly
        };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get a patient by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<PatientDto>> GetPatient(Guid id)
    {
        var query = new GetPatientQuery { Id = id };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return HandleFailure(result, StatusCodes.Status404NotFound);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Create a new patient
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PatientDto>> CreatePatient([FromBody] CreatePatientCommand command)
    {
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetPatients), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>
    /// Update an existing patient
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<PatientDto>> UpdatePatient(Guid id, [FromBody] UpdatePatientCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Delete a patient. Refused with a message naming what is attached whenever anything is — the pre-check
    /// counts invoices and treatment plans explicitly, since neither has a foreign key to Patients and no
    /// database constraint has ever fired for them.
    /// </summary>
    [HttpDelete("{id}")]
    // The one irreversible operation on a patient record. Archiving is the escape hatch every other role has.
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> DeletePatient(Guid id)
    {
        var result = await _mediator.Send(new DeletePatientCommand { Id = id });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return NoContent();
    }

    /// <summary>
    /// What blocks this patient's deletion, and whether archiving is available instead. Read when the confirm
    /// dialog opens so the user learns the answer before clicking, not after.
    /// </summary>
    [HttpGet("{id}/deletion-check")]
    // Gated with the delete it precedes, not one step looser: it exists to fill that confirm dialog, and a role
    // that cannot reach the button has no use for the answer.
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<PatientDeletionCheckDto>> GetDeletionCheck(Guid id)
    {
        var result = await _mediator.Send(new GetPatientDeletionCheckQuery { PatientId = id });

        if (result.IsFailure)
        {
            return HandleFailure(result, StatusCodes.Status404NotFound);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Archive a patient — hidden from lists, search, recall and every picker, nothing destroyed, reversible.
    /// The escape hatch that keeps deletion refusable: this app has no merge and no soft delete, so without it
    /// a duplicate patient with a single booking could never be removed from the list. Refused when a balance
    /// is due or a visit is booked.
    /// </summary>
    [HttpPost("{id}/archive")]
    // Nothing is destroyed, but the patient leaves every list, search and picker — indistinguishable from gone
    // to whoever looks for them next.
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult<PatientDto>> ArchivePatient(Guid id, [FromBody] ArchivePatientRequest? request)
    {
        var result = await _mediator.Send(new ArchivePatientCommand { Id = id, Reason = request?.Reason });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>Restore an archived patient everywhere.</summary>
    [HttpPost("{id}/unarchive")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult<PatientDto>> UnarchivePatient(Guid id)
    {
        var result = await _mediator.Send(new UnarchivePatientCommand { Id = id });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }
}
