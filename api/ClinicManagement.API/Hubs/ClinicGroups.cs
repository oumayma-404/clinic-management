namespace ClinicManagement.API.Hubs;

/// <summary>
/// Single source of truth for the SignalR group name a clinic's connections are joined to.
/// Both the hub (which adds connections to the group) and the notifier (which broadcasts to it)
/// resolve the name here so the two can never drift apart.
/// </summary>
public static class ClinicGroups
{
    public static string Name(Guid clinicId) => $"clinic-{clinicId}";

    /// <summary>One person's tabs — what a staff chat message is sent to, so a third colleague's browser stays quiet.</summary>
    public static string User(Guid clinicId, string userId) => $"user-{clinicId}-{userId}";
}
