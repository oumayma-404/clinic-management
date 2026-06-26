using System.Security.Claims;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using Microsoft.AspNetCore.Http;

namespace ClinicManagement.Application.Common.Services;

/// <summary>
/// Extracts clinic and user information from JWT claims
/// </summary>
public class ClinicContext : IClinicContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClinicContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? GetClinicId()
    {
        // Try multiple claim types/names for clinic_id (in order of likelihood)
        var clinicIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("https://clinic-management.com/clinic_id")?.Value
            ?? _httpContextAccessor.HttpContext?.User?.FindFirst("clinic_id")?.Value;
        
        if (string.IsNullOrEmpty(clinicIdClaim) || !Guid.TryParse(clinicIdClaim, out var clinicId))
        {
            return null;
        }
        return clinicId;
    }

    public string? GetUserRole()
    {
        // Try multiple claim types/names for role (in order of likelihood)
        return _httpContextAccessor.HttpContext?.User?.FindFirst("https://clinic-management.com/role")?.Value
            ?? _httpContextAccessor.HttpContext?.User?.FindFirst("role")?.Value
            ?? _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)?.Value;
    }

    public string? GetUserId()
    {
        return _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value;
    }

    public string? GetUserEmail()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return null;

        // Try multiple claim types/names for email (in order of likelihood)
        // Auth0 typically uses "email" claim
        var email = user.FindFirst("email")?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value
            ?? user.FindFirst("https://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value
            ?? user.FindFirst("https://clinic-management.com/email")?.Value;

        // If still not found, search through all claims for anything containing "email" (case-insensitive)
        if (string.IsNullOrWhiteSpace(email))
        {
            var emailClaim = user.Claims.FirstOrDefault(c => 
                c.Type.Contains("email", StringComparison.OrdinalIgnoreCase) && 
                !string.IsNullOrWhiteSpace(c.Value) &&
                c.Value.Contains("@"));
            email = emailClaim?.Value;
        }

        return email;
    }

    public bool BelongsToClinic(Guid clinicId)
    {
        var userClinicId = GetClinicId();
        return userClinicId.HasValue && userClinicId.Value == clinicId;
    }

    public void EnsureClinicAccess(Guid clinicId)
    {
        if (!BelongsToClinic(clinicId))
        {
            throw new ForbiddenAccessException("Access denied. You do not have permission to access this clinic's data.");
        }
    }
}



