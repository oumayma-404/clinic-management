using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Application.Features.Clinics.Queries;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.API.Models;
using System.Text.Json;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ClinicsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public ClinicsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Check if the current user has a clinic and get user status
    /// </summary>
    [HttpGet("user-status")]
    public async Task<IActionResult> GetUserStatus()
    {
        var query = new GetUserStatusQuery();
        var result = await _mediator.Send(query);
        
        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }
        
        return Ok(result);
    }

    /// <summary>
    /// Create a new clinic (first user/admin)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateClinic()
    {
        DoctorPersonalInfoDto? doctorInfo = null;
        Stream? logoFile = null;
        string? logoContentType = null;
        string name;
        string? address;
        string? phone;
        string? email;
        bool generateCode;
        string role;

        // Check if request is FormData (has logo) or JSON (no logo)
        if (Request.HasFormContentType)
        {
            // FormData request (has logo)
            var formRequest = await Request.ReadFormAsync();
            
            name = formRequest["name"].ToString();
            address = formRequest["address"].ToString();
            phone = formRequest["phone"].ToString();
            email = formRequest["email"].ToString();
            generateCode = formRequest["generateCode"].ToString().ToLowerInvariant() == "true";
            role = formRequest["role"].ToString();
            
            // Handle logo file
            var logo = formRequest.Files["logo"];
            if (logo != null && logo.Length > 0)
            {
                logoFile = logo.OpenReadStream();
                logoContentType = logo.ContentType;
            }
            
            // Parse DoctorInfo from JSON string
            var doctorInfoJson = formRequest["doctorInfoJson"].ToString();
            if (!string.IsNullOrWhiteSpace(doctorInfoJson))
            {
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    doctorInfo = JsonSerializer.Deserialize<DoctorPersonalInfoDto>(doctorInfoJson, options);
                }
                catch (Exception ex)
                {
                    return BadRequest(new { error = $"Invalid DoctorInfo format: {ex.Message}" });
                }
            }
        }
        else
        {
            // JSON request (no logo)
            var bodyRequest = await Request.ReadFromJsonAsync<Application.DTOs.CreateClinicRequest>();
            if (bodyRequest == null)
            {
                return BadRequest(new { error = "Request body is required" });
            }
            
            name = bodyRequest.Name;
            address = bodyRequest.Address;
            phone = bodyRequest.Phone;
            email = bodyRequest.Email;
            generateCode = bodyRequest.GenerateCode;
            role = bodyRequest.Role;
            doctorInfo = bodyRequest.DoctorInfo;
        }

        var command = new CreateClinicCommand
        {
            Name = name,
            Address = address,
            Phone = phone,
            Email = email,
            GenerateCode = generateCode,
            Role = role,
            DoctorInfo = doctorInfo,
            LogoFile = logoFile,
            LogoContentType = logoContentType
        };
        
        var result = await _mediator.Send(command);
        
        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }
        
        return CreatedAtAction(nameof(GetUserStatus), new { }, result);
    }

    /// <summary>
    /// Join an existing clinic using a clinic code
    /// </summary>
    [HttpPost("join")]
    public async Task<IActionResult> JoinClinic([FromBody] JoinClinicRequest request)
    {
        var command = new JoinClinicCommand
        {
            Code = request.Code,
            Role = request.Role,
            DoctorInfo = request.DoctorInfo
        };
        
        var result = await _mediator.Send(command);
        
        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }
        
        return Ok(result);
    }

    /// <summary>
    /// Update doctors for the current user's clinic
    /// </summary>
    [HttpPut("doctors")]
    public async Task<IActionResult> UpdateDoctors([FromBody] UpdateDoctorsRequest request)
    {
        var command = new UpdateDoctorsCommand
        {
            Doctors = request.Doctors
        };
        
        var result = await _mediator.Send(command);
        
        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }
        
        return Ok(result);
    }

    /// <summary>
    /// Update clinic information for the current user's clinic
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateClinic([FromForm] UpdateClinicRequest request)
    {
        var command = new UpdateClinicCommand
        {
            Name = request.Name,
            Address = request.Address,
            Phone = request.Phone,
            Email = request.Email,
            LogoFile = request.Logo?.OpenReadStream(),
            LogoContentType = request.Logo?.ContentType,
            MatriculeFiscal = request.MatriculeFiscal,
            VatApplicable = request.VatApplicable,
            VatRate = request.VatRate,
            StampDutyEnabled = request.StampDutyEnabled,
            StampDutyAmount = request.StampDutyAmount,
            TtnEInvoicingEnabled = request.TtnEInvoicingEnabled,
            TtnEnvironment = request.TtnEnvironment
        };
        
        var result = await _mediator.Send(command);
        
        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }
        
        return Ok(result);
    }

    /// <summary>
    /// Regenerate the clinic's self-registration code (admin-only, AC-4.5). Invalidates the
    /// old code for future staff registrations.
    /// </summary>
    [HttpPost("regenerate-code")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> RegenerateCode()
    {
        var result = await _mediator.Send(new RegenerateClinicCodeCommand());

        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Get the current clinic's reminder settings (admin-only, AC-1). Secret-masked — never returns the
    /// stored SMS API key / WhatsApp access token, only per-secret configured flags.
    /// </summary>
    [HttpGet("reminder-settings")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> GetReminderSettings(CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetClinicReminderSettingsQuery(), cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Update the current clinic's reminder settings (admin-only, AC-2). Secrets are write-only — an
    /// omitted/blank secret leaves the stored value unchanged; a provided one is encrypted and replaces it.
    /// </summary>
    [HttpPut("reminder-settings")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> UpdateReminderSettings(
        [FromBody] UpdateReminderSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var command = new UpdateClinicReminderSettingsCommand { Settings = request };
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Download clinic logo
    /// </summary>
    [HttpGet("logo")]
    public async Task<IActionResult> GetLogo(CancellationToken cancellationToken = default)
    {
        var query = new GetClinicLogoQuery();
        var result = await _mediator.Send(query, cancellationToken);
        
        if (!result.IsSuccess)
        {
            return HandleFailure(result, StatusCodes.Status404NotFound);
        }
        
        var logoDto = result.Value!;
        return File(logoDto.FileStream, logoDto.ContentType, "logo");
    }
}

