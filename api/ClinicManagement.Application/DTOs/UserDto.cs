namespace ClinicManagement.Application.DTOs;

public class UserDto
{
    public string Id { get; set; } = string.Empty; // Auth0 sub
    public Guid ClinicId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public DateTime CreatedAt { get; set; }
}





