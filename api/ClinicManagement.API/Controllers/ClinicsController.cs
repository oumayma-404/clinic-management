using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using MediatR;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Application.Features.Clinics.Queries;
using ClinicManagement.Application.Features.Messaging.Queries;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.API.Models;
using ClinicManagement.Infrastructure.Deployment;
using System.Text.Json;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
public class ClinicsController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    private readonly DeploymentProfile _deployment;

    public ClinicsController(IMediator mediator, IConfiguration configuration, DeploymentProfile deployment)
    {
        _mediator = mediator;
        _configuration = configuration;
        _deployment = deployment;
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
        string? logoFileName = null;
        long logoLength = 0;
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
                logoFileName = logo.FileName;
                logoLength = logo.Length;
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
            LogoFileName = logoFileName,
            LogoLength = logoLength,
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
    // Clinic configuration, and specifically the billing settings — matricule fiscal, TVA, timbre.
    // Every write on the tabs beside it was already admin-gated; this one was reachable by any authenticated user.
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> UpdateClinic([FromForm] UpdateClinicRequest request)
    {
        var command = new UpdateClinicCommand
        {
            Name = request.Name,
            // Band A — the `*Specified` flags travel with the values: a form key that was SENT and empty clears the
            // field, and one that was absent leaves it. See `UpdateClinicRequest` for why the two cannot be told
            // apart from the value alone.
            Address = request.Address,
            AddressSpecified = request.AddressSpecified,
            City = request.City,
            CitySpecified = request.CitySpecified,
            Phone = request.Phone,
            PhoneSpecified = request.PhoneSpecified,
            Email = request.Email,
            EmailSpecified = request.EmailSpecified,
            LogoFile = request.Logo?.OpenReadStream(),
            LogoFileName = request.Logo?.FileName,
            LogoLength = request.Logo?.Length ?? 0,
            MatriculeFiscal = request.MatriculeFiscal,
            MatriculeFiscalSpecified = request.MatriculeFiscalSpecified,
            VatApplicable = request.VatApplicable,
            VatRate = request.VatRate,
            StampDutyEnabled = request.StampDutyEnabled,
            StampDutyAmount = request.StampDutyAmount,
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
    /// <para>⚠️ That claim was <b>false</b> for a while and is worth knowing why. A failed row's
    /// <c>FailureReason</c> used to carry up to 200 bytes of the gateway's own response body — and the gateway URL
    /// is set by the clinic, so this endpoint returned whatever a tenant-chosen address answered, to every clinic
    /// role. <c>HttpReminderChannelSender</c> now reports the status code only and logs the body server-side, which
    /// is what makes the sentence above true again.</para>
    /// <para>All four filters are optional and <b>tolerant</b>: an unknown status or channel is ignored rather than
    /// refused, so a stale bookmark shows the full log instead of a French error about a query parameter.</para>
    /// </remarks>
    [HttpGet("reminder-log")]
    [Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
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
    /// « Forfait de rappels WhatsApp » — what this cabinet has left this Tunisian month (US-2, AC-2.1).
    /// </summary>
    /// <remarks>
    /// <para><b><c>AnyClinicRole</c>, deliberately</b> (AC-2.2). Reception is who meets a refused « Relancer »
    /// chairside, and this is the read that explains it — the same exception « Abonnement » makes, and for the same
    /// reason: none of these figures is clinic revenue (FR-2).</para>
    /// <para><b>404 <i>before</i> the mediator where the deployment does not sell vendor messaging</b> (AC-1.6,
    /// EC-16), on <c>SubscriptionController</c>'s precedent — so on the other two deployment kinds the handler, its
    /// repository and the allowance policy are never resolved. « Absent », not « present and refusing ».</para>
    /// <para>⚠️ Gated on <c>SellsVendorMessaging</c> and <b>not</b> on whether the Meta credentials are configured:
    /// an allowance a cabinet cannot yet spend is still a real allowance, and collapsing the two would make a
    /// missing <c>Meta:AppId</c> look like a deployment that does not do this at all.</para>
    /// </remarks>
    [HttpGet("reminder-allowance")]
    [Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
    public async Task<IActionResult> GetReminderAllowance(CancellationToken cancellationToken = default)
    {
        if (!_deployment.SellsVendorMessaging)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new GetReminderAllowanceQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result) : HandleFailure(result);
    }

    /// <summary>
    /// The cabinet's consumption month by month — this Tunisian month and the twelve before it (AC-2.3).
    /// </summary>
    /// <remarks>
    /// Same policy and same 404 as <see cref="GetReminderAllowance"/>. Months below the D-5 floor are
    /// <b>omitted</b> rather than reported unmeasured, so a cabinet that predates the rollout is not told we failed
    /// to count months that nobody was counting.
    /// </remarks>
    [HttpGet("reminder-allowance/history")]
    [Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
    public async Task<IActionResult> GetReminderAllowanceHistory(CancellationToken cancellationToken = default)
    {
        if (!_deployment.SellsVendorMessaging)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new GetReminderAllowanceHistoryQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result) : HandleFailure(result);
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
        if (!_deployment.ExposesMetaOnboarding)
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
        if (!_deployment.ExposesMetaOnboarding)
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
    // Every screen with a header renders it, for every role.
    [Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
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

