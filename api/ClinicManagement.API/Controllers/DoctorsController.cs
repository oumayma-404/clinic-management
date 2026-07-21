using ClinicManagement.API.Models;
using ClinicManagement.Application.Features.Doctors.Commands;
using ClinicManagement.Application.Features.Doctors.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Per-doctor document identity (FR-2.5 / FR-3.1): CNOMDT order number + cachet image. <c>/me</c> targets
/// the caller's own record; <c>/{id}</c> targets a specific doctor (own-or-admin, enforced in the handler).
/// </summary>
[ApiController]
[Route("api/doctors")]
[Authorize]
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

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMyProfile([FromForm] UpdateDoctorProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(ToCommand(null, request), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }

    [HttpPut("{id:guid}")]
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
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value!.FileStream, result.Value.ContentType, $"cachet-{id}");
    }

    private static UpdateDoctorProfileCommand ToCommand(Guid? doctorId, UpdateDoctorProfileRequest request) => new()
    {
        DoctorId = doctorId,
        OrdreNumberCnomdt = request.OrdreNumberCnomdt,
        CachetStream = request.Cachet?.OpenReadStream(),
        CachetContentType = request.Cachet?.ContentType,
        RemoveCachet = request.RemoveCachet
    };
}
