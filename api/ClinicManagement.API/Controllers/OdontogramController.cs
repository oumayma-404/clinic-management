using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Queries;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/patients/{patientId:guid}/odontogram")]
[Authorize]
public class OdontogramController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public OdontogramController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get a patient's odontogram: all recorded tooth treatments (many-per-tooth). Read-only — tooth
    /// conditions are set through the dental-record ("ajouter un acte médical") flow, not here.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ToothStateDto>>> GetOdontogram(Guid patientId)
    {
        var result = await _mediator.Send(new GetOdontogramQuery { PatientId = patientId });
        return result.IsFailure ? HandleFailure(result, StatusCodes.Status404NotFound) : Ok(result.Value);
    }
}
