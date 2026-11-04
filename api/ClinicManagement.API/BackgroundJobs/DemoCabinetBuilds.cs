using System.Collections.Concurrent;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// demo-cabinet: the cabinets a rebuild is writing right now. <see cref="AppointmentProgressJob"/> leaves them alone —
/// every visit the scenario books is already in the past, so its minute tick otherwise closes the one a fiche is about
/// to close, and the fiche's own completion and note d'honoraires then fail on the stale version.
/// </summary>
public sealed class DemoCabinetBuilds
{
    private readonly ConcurrentDictionary<Guid, byte> _building = new();

    /// <summary>One rebuild at a time in this process: the first-boot build and the nightly one share it.</summary>
    public SemaphoreSlim RebuildGate { get; } = new(1, 1);

    /// <summary>The clinic day the cabinet visitors enter was built for — stale means a nightly rebuild failed.</summary>
    public DateOnly? CurrentBuiltOn { get; private set; }

    public IDisposable Begin(Guid clinicId)
    {
        _building[clinicId] = 0;
        return new Ending(this, clinicId);
    }

    public bool IsBuilding(Guid clinicId) => _building.ContainsKey(clinicId);

    public void Opened(DateOnly builtOn) => CurrentBuiltOn = builtOn;

    private sealed class Ending(DemoCabinetBuilds builds, Guid clinicId) : IDisposable
    {
        public void Dispose() => builds._building.TryRemove(clinicId, out _);
    }
}
