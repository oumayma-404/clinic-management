namespace ClinicManagement.Application.DTOs;

/// <summary>The free start times of a visit's practitioner, day by day — what « Reporter » offers.</summary>
public sealed class FreeSlotsDto
{
    public required IReadOnlyList<FreeDayDto> Days { get; init; }
}

public sealed class FreeDayDto
{
    /// <summary>The clinic-local day, <c>YYYY-MM-DD</c>.</summary>
    public required string Date { get; init; }

    /// <summary>The <c>FreeDayState</c> member's own name: <c>Open</c>, <c>Full</c>, <c>Closed</c>, <c>Leave</c> or <c>Unrestricted</c>.</summary>
    public required string State { get; init; }

    /// <summary>Clinic-local start times, <c>HH:mm</c>, in order.</summary>
    public required IReadOnlyList<string> Starts { get; init; }
}
