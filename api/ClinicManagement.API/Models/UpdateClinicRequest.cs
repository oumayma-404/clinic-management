using Microsoft.AspNetCore.Http;

namespace ClinicManagement.API.Models;

public class UpdateClinicRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public IFormFile? Logo { get; set; }
}



