namespace ClinicManagement.Domain.Enums;

/// <summary>Where a PC de secours is in its life (<c>clinic-pc-copy</c>). Persisted as int: append, never insert.</summary>
public enum ClinicRelayStatus
{
    /// <summary>A pairing code was issued; no PC has used it yet.</summary>
    Pairing = 0,

    /// <summary>Paired; the first copy is under way.</summary>
    Seeding = 1,

    /// <summary>The first copy is complete; it follows the change log.</summary>
    Active = 2,

    /// <summary>Stopped for good — see <see cref="ClinicRelayRetirement"/> for why.</summary>
    Retired = 3,
}

/// <summary>Why a PC de secours stopped (AC-8.1–8.5, EC-9, AC-9.3). Persisted as int: append, never insert.</summary>
public enum ClinicRelayRetirement
{
    Retired = 0,
    LostOrStolen = 1,
    Abandoned = 2,
    Uninstalled = 3,
    Promoted = 4,
}

/// <summary>What happened to a key in the clinic's change log. Persisted as int.</summary>
public enum ClinicChangeOp
{
    Upsert = 0,
    Delete = 1,
}

/// <summary>Which side wrote a change: the cloud, or the PC de secours while it held the lease. Persisted as int.</summary>
public enum ClinicChangeOrigin
{
    Cloud = 0,
    Relay = 1,
}
