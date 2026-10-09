using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>The words every PC de secours state is shown in (<c>clinic-pc-copy</c> FR-2, AC-9.1).</summary>
public class RelayLabelsTests
{
    // 2026-10-07 09:12 UTC = 10:12 in Tunis.
    private static readonly DateTime Now = new(2026, 10, 7, 9, 12, 0, DateTimeKind.Utc);

    // Derived over the enum: a new state that one of the three forgets throws inside the console list, the card and
    // the bell at once — on the read every screen depends on.
    [Fact]
    public void Every_State_Has_A_Key_A_Sentence_And_A_Short_Label()
    {
        var states = Enum.GetValues<ClinicRelayState>();
        Assert.True(states.Length >= 12);

        foreach (var state in states)
        {
            var reading = new ClinicRelayHealthReading(state, Now.AddMinutes(-30), 40);
            Assert.False(string.IsNullOrWhiteSpace(RelayLabels.Key(state)));
            Assert.False(string.IsNullOrWhiteSpace(RelayLabels.Sentence(reading, null, Now)));
            Assert.False(string.IsNullOrWhiteSpace(RelayLabels.Short(reading, Now)));
        }

        Assert.Equal(states.Length, states.Select(RelayLabels.Key).Distinct().Count());
    }

    // AC-9.1's words, in Tunisian time.
    [Theory]
    [InlineData(ClinicRelayState.None, "Aucun")]
    [InlineData(ClinicRelayState.Ready, "Prêt")]
    [InlineData(ClinicRelayState.Late, "En retard de 30 min")]
    [InlineData(ClinicRelayState.Off, "Éteint depuis 09:42")]
    [InlineData(ClinicRelayState.Mismatch, "Ne correspond pas")]
    [InlineData(ClinicRelayState.Retired, "Retiré")]
    [InlineData(ClinicRelayState.Stopped, "Copie arrêtée depuis 09:42")]
    public void The_Console_Says_It_Short(ClinicRelayState state, string expected)
    {
        Assert.Equal(expected, RelayLabels.Short(new ClinicRelayHealthReading(state, Now.AddMinutes(-30), null), Now));
    }
}
