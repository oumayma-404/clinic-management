using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

public class StaffChatReadMarkerTests
{
    private static readonly DateTime Nine = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_Marker_Only_Moves_Forward() // a slower tab must never un-read a newer message
    {
        var marker = new StaffChatReadMarker(Guid.NewGuid(), "local|u", "clinic", Nine);

        Assert.False(marker.Advance(Nine.AddMinutes(-5)));
        Assert.False(marker.Advance(Nine));
        Assert.Equal(Nine, marker.LastReadAt);

        Assert.True(marker.Advance(Nine.AddMinutes(1)));
        Assert.Equal(Nine.AddMinutes(1), marker.LastReadAt);
    }
}
