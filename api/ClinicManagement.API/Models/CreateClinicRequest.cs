using Microsoft.AspNetCore.Http;
using ClinicManagement.Application.DTOs;

namespace ClinicManagement.API.Models;

public class CreateClinicRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool GenerateCode { get; set; } = true;
    public string Role { get; set; } = "doctor";
    public IFormFile? Logo { get; set; }
    // Note: DoctorInfo will be sent as JSON string in FormData
    public string? DoctorInfoJson { get; set; }
}



