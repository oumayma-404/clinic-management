using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using MediatR;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Application.Features.Clinics.Queries;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.API.Models;
using ClinicManagement.Infrastructure.Auth;
using System.Text.Json;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ClinicsController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    public ClinicsController(IMediator mediator, IConfiguration configuration)
    {
        _mediator = mediator;
        _configuration = configuration;
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
            return HandleFailure(result);
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
        string? city;
        string? phone;
        string? email;
        bool generateCode;
        string role;
        string? workingHoursJson = null;

        // Check if request is FormData (has logo) or JSON (no logo)
        if (Request.HasFormContentType)
        {
            // FormData request (has logo)
            var formRequest = await Request.ReadFormAsync();
            
            name = formRequest["name"].ToString();
            address = formRequest["address"].ToString();
            city = formRequest["city"].ToString();
            phone = formRequest["phone"].ToString();
            email = formRequest["email"].ToString();
            generateCode = formRequest["generateCode"].ToString().ToLowerInvariant() == "true";
            role = formRequest["role"].ToString();
            var whForm = formRequest["workingHoursJson"].ToString();
            workingHoursJson = string.IsNullOrWhiteSpace(whForm) ? null : whForm;

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
                catch (Exception)
                {
                    return Failure("Les informations du praticien sont mal formées.");
                }
            }
        }
        else
        {
            // JSON request (no logo)
            var bodyRequest = await Request.ReadFromJsonAsync<Application.DTOs.CreateClinicRequest>();
            if (bodyRequest == null)
            {
                return Failure("Le corps de la requête est requis.");
            }
            
            name = bodyRequest.Name;
            address = bodyRequest.Address;
            city = bodyRequest.City;
            phone = bodyRequest.Phone;
            email = bodyRequest.Email;
            generateCode = bodyRequest.GenerateCode;
            role = bodyRequest.Role;
            doctorInfo = bodyRequest.DoctorInfo;
            workingHoursJson = bodyRequest.WorkingHoursJson;
        }

        var command = new CreateClinicCommand
        {
            Name = name,
            Address = address,
            City = city,
            Phone = phone,
            Email = email,
            GenerateCode = generateCode,
            Role = role,
            DoctorInfo = doctorInfo,
            LogoFile = logoFile,
            LogoContentType = logoContentType,
            WorkingHoursJson = workingHoursJson
        };
        
        var result = await _mediator.Send(command);
        
        if (!result.IsSuccess)
        {
            return HandleFailure(result);
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
            return HandleFailure(result);
        }
        
        return Ok(result);
    }

    /// <summary>
    /// Update doctors for the current user's clinic
    /// </summary>
    // Admin-only: rewriting the practitioner roster is clinic-wide configuration, and it was reachable by any
    // authenticated user including a secretary (audit § 2, finding 8).
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
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
            return HandleFailure(result);
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
            City = request.City,
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
            TtnEnvironment = request.TtnEnvironment,
            WorkingHoursJson = request.WorkingHoursJson,
            Version = request.Version
        };

        var result = await _mediator.Send(command);
        
        if (!result.IsSuccess)
        {
            return HandleFailure(result);
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
            return HandleFailure(result);
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
            return HandleFailure(result);
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
            return HandleFailure(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Get the recent reminder outbox rows for the current clinic with their delivery status (admin-only,
    /// AC-3) — so a failed reminder is noticed instead of vanishing. Recipient phone is masked.
    /// </summary>
    [HttpGet("reminder-status")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> GetReminderStatus(
        [FromQuery] int take = GetClinicReminderStatusQuery.DefaultTake, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetClinicReminderStatusQuery { Take = take }, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// The « Rappels » page: one filtered, paged view of the reminder outbox plus the clinic's three counters.
    /// </summary>
    /// <remarks>
    /// <para><b>Deliberately not <c>AdminOnly</c></b>, unlike <c>reminder-status</c> above. Reading the log is what
    /// a secretary fielding « je n'ai reçu aucun message » needs to do, and a row carries a patient name and a
    /// phone masked to its last two digits — no credentials, no template bodies, nothing the admin gate was
    /// protecting. Every <b>write</b> to the channel settings stays admin-gated.</para>
    /// <para>All four filters are optional and <b>tolerant</b>: an unknown status or channel is ignored rather than
    /// refused, so a stale bookmark shows the full log instead of a French error about a query parameter.</para>
    /// </remarks>
    [HttpGet("reminder-log")]
    public async Task<IActionResult> GetReminderLog(
        [FromQuery] string? status = null,
        [FromQuery] string? channel = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetClinicReminderLogQuery
            {
                Status = status,
                Channel = channel,
                From = from,
                To = to,
                Page = page,
                PageSize = pageSize,
            },
            cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Connect the current clinic's WhatsApp via Meta Embedded Signup (admin-only, Cloud-only). Exchanges the
    /// one-time code, subscribes the app, registers the phone number, and stores the encrypted credentials —
    /// atomically. Returns the secret-masked settings (status Connected). 404 in Local mode.
    /// </summary>
    [HttpPost("whatsapp/connect")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> ConnectWhatsApp(
        [FromBody] ConnectWhatsAppRequest request, CancellationToken cancellationToken = default)
    {
        if (LocalAuthConfig.IsLocalMode(_configuration))
        {
            return NotFound();
        }

        var command = new ConnectClinicWhatsAppCommand { Request = request };
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Disconnect the current clinic's WhatsApp (admin-only, Cloud-only). Clears the stored credentials,
    /// disables the channel and resets the status to NotConnected (best-effort Meta unsubscribe). 404 in Local.
    /// </summary>
    [HttpDelete("whatsapp/connect")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> DisconnectWhatsApp(CancellationToken cancellationToken = default)
    {
        if (LocalAuthConfig.IsLocalMode(_configuration))
        {
            return NotFound();
        }

        var result = await _mediator.Send(new DisconnectClinicWhatsAppCommand(), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
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

