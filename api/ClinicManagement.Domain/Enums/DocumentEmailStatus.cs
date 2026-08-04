namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Lifecycle of one outbound document email. <see cref="Sent"/> and <see cref="Failed"/> are terminal;
/// <see cref="Queued"/> is what the dispatcher scans for.
/// </summary>
public enum DocumentEmailStatus
{
    Queued = 1,
    Sent = 2,
    Failed = 3
}
