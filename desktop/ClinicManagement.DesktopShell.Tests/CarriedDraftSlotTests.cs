using System;
using ClinicManagement.DesktopShell;
using Xunit;

namespace ClinicManagement.DesktopShell.Tests;

/// <summary>D23 — the form state a window carries across a switch of server (AC-3.2).</summary>
public class CarriedDraftSlotTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_Carried_Form_Is_Handed_Over_Once()
    {
        var slot = new CarriedDraftSlot();
        slot.Carry("{\"v\":1}", Now);

        Assert.Equal("{\"v\":1}", slot.Take(Now.AddMinutes(1)));
        Assert.Null(slot.Take(Now.AddMinutes(1)));
    }

    [Fact]
    public void The_Latest_Typing_Replaces_The_Earlier_And_Empty_Forgets()
    {
        var slot = new CarriedDraftSlot();
        slot.Carry("a", Now);
        slot.Carry("b", Now);
        Assert.Equal("b", slot.Take(Now));

        slot.Carry("c", Now);
        slot.Carry("", Now);
        Assert.Null(slot.Take(Now));
    }

    [Fact]
    public void An_Old_Or_Oversized_Draft_Is_Not_Handed_Back()
    {
        var slot = new CarriedDraftSlot();
        slot.Carry("a", Now);
        Assert.Null(slot.Take(Now + CarriedDraftSlot.MaxAge + TimeSpan.FromSeconds(1)));

        slot.Carry("a", Now);
        slot.Carry(new string('x', CarriedDraftSlot.MaxChars + 1), Now);
        Assert.Null(slot.Take(Now));
    }
}
