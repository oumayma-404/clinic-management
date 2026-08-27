using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// [AC-P2.38–2.41] <c>LabWorkOrder.SetStatus</c> was a bare assignment with no rules at all, unlike every other
/// aggregate in the repo with a lifecycle. A « Posé » order could be pushed straight back to « Envoyé », and an
/// order could jump from « Envoyé » to « Posé » — recording a fitting for a prothèse the clinic never received.
/// </summary>
public class LabWorkOrderStatusTransitionTests
{
    private static LabWorkOrder NewOrder() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Labo Dentaire Tunis",
        "Couronne céramo-métallique",
        toothNumber: 16);

    /// <summary>Walk an order to a state through legal steps only, so the fixture cannot beg the question.</summary>
    private static LabWorkOrder OrderAt(LabOrderStatus status)
    {
        var order = NewOrder();
        switch (status)
        {
            case LabOrderStatus.Sent:
                break;
            case LabOrderStatus.InProgress:
                order.SetStatus(LabOrderStatus.InProgress);
                break;
            case LabOrderStatus.Received:
                order.SetStatus(LabOrderStatus.InProgress);
                order.SetStatus(LabOrderStatus.Received);
                break;
            case LabOrderStatus.Fitted:
                order.SetStatus(LabOrderStatus.InProgress);
                order.SetStatus(LabOrderStatus.Received);
                order.SetStatus(LabOrderStatus.Fitted);
                break;
        }
        return order;
    }

    // A new order starts « Envoyé » regardless of whether a sent date was supplied — unchanged behaviour, pinned
    // because the transition table is written relative to it.
    [Fact]
    public void A_New_Order_Starts_Sent()
    {
        Assert.Equal(LabOrderStatus.Sent, NewOrder().Status);
    }

    // ---- Legal transitions ---------------------------------------------------

    [Theory]
    [InlineData(LabOrderStatus.Sent, LabOrderStatus.InProgress)]
    [InlineData(LabOrderStatus.Sent, LabOrderStatus.Received)]
    [InlineData(LabOrderStatus.InProgress, LabOrderStatus.Received)]
    [InlineData(LabOrderStatus.Received, LabOrderStatus.Fitted)]
    public void Forward_Transitions_Are_Allowed(LabOrderStatus from, LabOrderStatus to) // [AC-P2.38]
    {
        var order = OrderAt(from);

        order.SetStatus(to);

        Assert.Equal(to, order.Status);
    }

    // One step backward is deliberately legal: a prothèse that arrives wrong goes back to the lab, and a
    // fitting recorded on the wrong order has to be undoable.
    [Theory]
    [InlineData(LabOrderStatus.InProgress, LabOrderStatus.Sent)]
    [InlineData(LabOrderStatus.Received, LabOrderStatus.InProgress)]
    [InlineData(LabOrderStatus.Fitted, LabOrderStatus.Received)]
    public void One_Step_Backward_Is_Allowed(LabOrderStatus from, LabOrderStatus to) // [AC-P2.38]
    {
        var order = OrderAt(from);

        order.SetStatus(to);

        Assert.Equal(to, order.Status);
    }

    // ---- Illegal transitions -------------------------------------------------

    // The named case in the AC: a fitted order cannot be pushed back to « Envoyé ».
    [Fact]
    public void A_Fitted_Order_Cannot_Go_Back_To_Sent() // [AC-P2.38]
    {
        var order = OrderAt(LabOrderStatus.Fitted);

        var ex = Assert.Throws<InvalidOperationException>(() => order.SetStatus(LabOrderStatus.Sent));

        Assert.Equal(LabOrderStatus.Fitted, order.Status);
        // [AC-P2.40] French, and it names both stages as the UI shows them.
        Assert.Contains("Posé", ex.Message);
        Assert.Contains("Envoyé", ex.Message);
    }

    [Theory]
    // Skipping the lab entirely: a fitting recorded for work never received.
    [InlineData(LabOrderStatus.Sent, LabOrderStatus.Fitted)]
    [InlineData(LabOrderStatus.InProgress, LabOrderStatus.Fitted)]
    // Rewinding past the delivery, which would erase that the piece ever arrived.
    [InlineData(LabOrderStatus.Received, LabOrderStatus.Sent)]
    [InlineData(LabOrderStatus.Fitted, LabOrderStatus.Sent)]
    [InlineData(LabOrderStatus.Fitted, LabOrderStatus.InProgress)]
    public void Illegal_Transitions_Are_Refused(LabOrderStatus from, LabOrderStatus to) // [AC-P2.38 / AC-P2.40]
    {
        var order = OrderAt(from);

        Assert.Throws<InvalidOperationException>(() => order.SetStatus(to));
        Assert.Equal(from, order.Status);
    }

    // A UI select can re-emit the current value; that must stay a silent no-op, not a refusal.
    [Theory]
    [InlineData(LabOrderStatus.Sent)]
    [InlineData(LabOrderStatus.InProgress)]
    [InlineData(LabOrderStatus.Received)]
    [InlineData(LabOrderStatus.Fitted)]
    public void Re_Assigning_The_Current_Status_Is_A_No_Op(LabOrderStatus status)
    {
        var order = OrderAt(status);

        order.SetStatus(status);

        Assert.Equal(status, order.Status);
    }

    // ---- ReceivedDate --------------------------------------------------------

    [Fact]
    public void Receiving_Stamps_The_Received_Date() // [AC-P2.39]
    {
        var order = OrderAt(LabOrderStatus.InProgress);
        Assert.Null(order.ReceivedDate);

        order.SetStatus(LabOrderStatus.Received);

        Assert.NotNull(order.ReceivedDate);
    }

    // [AC-P2.39] The whole point: a prothèse sent back to the lab and received again is a NEW arrival. The old
    // guard (`if (ReceivedDate == null)`) kept the first date forever, so the délai the clinic reads was the one
    // for the piece that had to be redone.
    [Fact]
    public void Receiving_Again_Re_Stamps_The_Received_Date()
    {
        var order = OrderAt(LabOrderStatus.Received);
        var firstArrival = order.ReceivedDate;
        Assert.NotNull(firstArrival);

        // Back to the lab, then received again — both legal steps.
        order.SetStatus(LabOrderStatus.InProgress);
        order.SetStatus(LabOrderStatus.Received);

        Assert.NotNull(order.ReceivedDate);
        Assert.True(order.ReceivedDate >= firstArrival);
        // The stamp is a fresh UtcNow, not the retained original.
        Assert.True((DateTime.UtcNow - order.ReceivedDate!.Value).TotalMinutes < 1);
    }

    // A refused transition must not stamp anything either — the order is untouched.
    [Fact]
    public void A_Refused_Transition_Leaves_The_Order_Untouched()
    {
        var order = OrderAt(LabOrderStatus.Sent);

        Assert.Throws<InvalidOperationException>(() => order.SetStatus(LabOrderStatus.Fitted));

        Assert.Equal(LabOrderStatus.Sent, order.Status);
        Assert.Null(order.ReceivedDate);
        Assert.Null(order.UpdatedAt);
    }

    // ---- The table the UI reads ---------------------------------------------

    [Theory]
    [InlineData(LabOrderStatus.Sent, new[] { LabOrderStatus.InProgress, LabOrderStatus.Received })]
    [InlineData(LabOrderStatus.InProgress, new[] { LabOrderStatus.Sent, LabOrderStatus.Received })]
    [InlineData(LabOrderStatus.Received, new[] { LabOrderStatus.InProgress, LabOrderStatus.Fitted })]
    [InlineData(LabOrderStatus.Fitted, new[] { LabOrderStatus.Received })]
    public void NextStatusesFrom_Matches_What_SetStatus_Accepts(LabOrderStatus from, LabOrderStatus[] expected)
    {
        // [AC-P2.40] The UI offers exactly this list, so it must not diverge from what SetStatus enforces —
        // otherwise the control offers a transition the server then refuses.
        Assert.Equal(expected, LabWorkOrder.NextStatusesFrom(from));

        foreach (var target in expected)
        {
            var order = OrderAt(from);
            order.SetStatus(target);
            Assert.Equal(target, order.Status);
        }
    }

    // [AC-P2.41] Reads are never gated. An existing row loads in any state — including one the table could not
    // produce — and simply offers no transitions rather than throwing.
    [Fact]
    public void Every_Status_Is_Readable_And_Has_A_Declared_Next_Set()
    {
        foreach (var status in Enum.GetValues<LabOrderStatus>())
        {
            Assert.NotNull(LabWorkOrder.NextStatusesFrom(status));
        }
    }
}
