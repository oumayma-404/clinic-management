using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.API.Hubs;

/// <summary>
/// Who has the app open, counted in open hub connections per (clinic, user) — a person with two tabs stays « en ligne »
/// until both close. In memory: one API process per deployment, and a PC de secours counts its own connections.
/// </summary>
public sealed class StaffPresence : IStaffPresence
{
    private readonly Dictionary<(Guid ClinicId, string UserId), int> _connections = new();
    private readonly object _gate = new();

    public bool IsOnline(Guid clinicId, string userId)
    {
        lock (_gate)
        {
            return _connections.ContainsKey((clinicId, userId));
        }
    }

    /// <summary>Records one more connection; true when the person just came online.</summary>
    public bool Connected(Guid clinicId, string userId)
    {
        lock (_gate)
        {
            _connections.TryGetValue((clinicId, userId), out var count);
            _connections[(clinicId, userId)] = count + 1;
            return count == 0;
        }
    }

    /// <summary>Records one connection fewer; true when the person just went offline.</summary>
    public bool Disconnected(Guid clinicId, string userId)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue((clinicId, userId), out var count))
            {
                return false;
            }

            if (count > 1)
            {
                _connections[(clinicId, userId)] = count - 1;
                return false;
            }

            _connections.Remove((clinicId, userId));
            return true;
        }
    }
}
