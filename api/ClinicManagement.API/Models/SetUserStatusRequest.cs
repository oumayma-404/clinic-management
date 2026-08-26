namespace ClinicManagement.API.Models;

public class SetUserStatusRequest
{
    public bool IsActive { get; set; }

    /// <summary>The version the client read — see <c>SetUserActiveCommand.Version</c>. 0 skips the check.</summary>
    public uint Version { get; set; }
}
