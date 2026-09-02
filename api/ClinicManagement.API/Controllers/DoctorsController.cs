using ClinicManagement.API.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Doctors.Commands;
using ClinicManagement.Application.Features.Doctors.Queries;
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
