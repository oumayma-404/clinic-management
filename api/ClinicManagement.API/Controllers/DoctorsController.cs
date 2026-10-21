using ClinicManagement.API.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Doctors.Commands;
using ClinicManagement.Application.Features.Doctors.Queries;
using ClinicManagement.Application.Features.DoctorLeaves.Commands;
using ClinicManagement.Application.Features.DoctorLeaves;
using ClinicManagement.Application.Features.DoctorLeaves.Queries;
using ClinicManagement.Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ClinicManagement.Application.Common.Files;
using Microsoft.AspNetCore.Mvc;
using ClinicManagement.Application.Common.Authorization;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Per-doctor document identity (FR-2.5 / FR-3.1): CNOMDT order number + cachet image. <c>/me</c> targets
/// the caller's own record; <c>/{id}</c> targets a specific doctor (own-or-admin, enforced in the handler).
/// </summary>
[ApiController]
[Route("api/doctors")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class DoctorsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public DoctorsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyDoctorProfileQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result, StatusCodes.Status404NotFound);
    }

    // The catalog's ceiling for THIS door, per action. Without it ASP.NET's default 30 MB body limit is the real
    // one, so a file between this door's cap and that default dies on a framework 413 the app never sees and
    // cannot explain in French — `PatientFilesController.UploadFile`'s documented reason, one door over.
    // ⚠️ The catalog CONST, not `FileUploadProfile.ProfileImage.MaxBytes`: an attribute argument has to be a
    // compile-time constant, which is the same reason `MaxBytesAcrossCatalog` exists. `FileTypeCatalogTests`
    // pins the two together so this cannot fall behind the door it is supposed to size.
    [RequestSizeLimit(FileTypeCatalog.ProfileImageBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = FileTypeCatalog.ProfileImageBytes)]
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMyProfile([FromForm] UpdateDoctorProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(ToCommand(null, request), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }

    // `/me` above is « Mon profil » and open to every role; `/{id}` edits *another* practitioner's document
    // identity — the CNOMDT order number and the cachet that signs ordonnances and certificats.
    // The catalog's ceiling for THIS door, per action. Without it ASP.NET's default 30 MB body limit is the real
    // one, so a file between this door's cap and that default dies on a framework 413 the app never sees and
    // cannot explain in French — `PatientFilesController.UploadFile`'s documented reason, one door over.
    // ⚠️ The catalog CONST, not `FileUploadProfile.ProfileImage.MaxBytes`: an attribute argument has to be a
    // compile-time constant, which is the same reason `MaxBytesAcrossCatalog` exists. `FileTypeCatalogTests`
    // pins the two together so this cannot fall behind the door it is supposed to size.
    [RequestSizeLimit(FileTypeCatalog.ProfileImageBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = FileTypeCatalog.ProfileImageBytes)]
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<IActionResult> UpdateProfile(Guid id, [FromForm] UpdateDoctorProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(ToCommand(id, request), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }

    [HttpGet("{id:guid}/cachet")]
    public async Task<IActionResult> GetCachet(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetDoctorCachetQuery { DoctorId = id }, cancellationToken);
        if (!result.IsSuccess)
        {
            return HandleFailure(result, StatusCodes.Status404NotFound);
        }

        // Security (FR-3.1): the cachet is a user-uploaded blob served from the app origin. Prevent MIME
        // sniffing and force a download disposition so it can never be interpreted as an inline document
        // (defence-in-depth on top of the upload-time PNG/JPEG allow-list + magic-byte check).
        // (Global SecurityHeadersMiddleware now sets nosniff on every response — AC-12.9.)
        return File(result.Value!.FileStream, result.Value.ContentType, $"cachet-{id}");
    }

    /// <summary>Get a dentist's per-practitioner working hours (AC-3.3). Empty = no override (clinic hours apply).</summary>
    [HttpGet("{id:guid}/working-hours")]
    public async Task<ActionResult<IEnumerable<WorkingDayDto>>> GetWorkingHours(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetDoctorWorkingHoursQuery { DoctorId = id }, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusCodes.Status404NotFound) : Ok(result.Value);
    }

    /// <summary>Set a dentist's per-practitioner working hours (AC-3.3). An empty list clears the override.</summary>
    [HttpPut("{id:guid}/working-hours")]
    // Reading the hours is what the agenda does for everyone; deciding them is the practitioner's own call.
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult<IEnumerable<WorkingDayDto>>> SetWorkingHours(Guid id, [FromBody] SetDoctorWorkingHoursCommand command, CancellationToken cancellationToken)
    {
        command.DoctorId = id;
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>The cabinet's congés touching the inclusive day window — what the agenda draws.</summary>
    [HttpGet("leaves")]
    public async Task<ActionResult<IEnumerable<DoctorLeaveDto>>> GetClinicLeaves(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetClinicDoctorLeavesQuery { FromDay = from, ToDay = to }, cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Every congé of one practitioner, earliest first.</summary>
    [HttpGet("{id:guid}/leaves")]
    public async Task<ActionResult<IEnumerable<DoctorLeaveDto>>> GetLeaves(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetDoctorLeavesQuery { DoctorId = id }, cancellationToken);
        return result.IsFailure ? HandleFailure(result, LeaveStatus(result)) : Ok(result.Value);
    }

    // Open to every role: reception records the congé it is told about; a doctor only their own (in the handler).
    [HttpPost("{id:guid}/leaves")]
    public async Task<ActionResult<DoctorLeaveSaveResultDto>> CreateLeave(
        Guid id, [FromBody] CreateDoctorLeaveCommand command, CancellationToken cancellationToken)
    {
        command.DoctorId = id;
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? HandleFailure(result, LeaveStatus(result)) : Ok(result.Value);
    }

    [HttpPut("{id:guid}/leaves/{leaveId:guid}")]
    public async Task<ActionResult<DoctorLeaveSaveResultDto>> UpdateLeave(
        Guid id, Guid leaveId, [FromBody] UpdateDoctorLeaveCommand command, CancellationToken cancellationToken)
    {
        command.DoctorId = id;
        command.LeaveId = leaveId;
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? HandleFailure(result, LeaveStatus(result)) : Ok(result.Value);
    }

    [HttpDelete("{id:guid}/leaves/{leaveId:guid}")]
    public async Task<IActionResult> DeleteLeave(Guid id, Guid leaveId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteDoctorLeaveCommand { DoctorId = id, LeaveId = leaveId }, cancellationToken);
        return result.IsFailure ? HandleFailure(result, LeaveStatus(result)) : NoContent();
    }

    // Every other congé refusal (dates, overlap, not your own) is a 400 with its French sentence.
    private static int LeaveStatus(Result result) =>
        result.Code == DoctorLeaveRules.NotFoundCode ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;

    private static UpdateDoctorProfileCommand ToCommand(Guid? doctorId, UpdateDoctorProfileRequest request) => new()
    {
        DoctorId = doctorId,
        OrdreNumberCnomdt = request.OrdreNumberCnomdt,
        CachetStream = request.Cachet?.OpenReadStream(),
        CachetFileName = request.Cachet?.FileName,
        CachetLength = request.Cachet?.Length ?? 0,
        RemoveCachet = request.RemoveCachet
    };
}
