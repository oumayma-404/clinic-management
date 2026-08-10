namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Why a queued outbound row was parked rather than sent — the machine-readable half of the French sentence both
/// outboxes already record (<c>clinic-subscription</c> FR-8). Shared by <c>Notification</c> and
/// <c>PushDelivery</c>, whose <c>Blocked</c> status it accompanies.
///
/// <para>⚠️ <b>It exists so the un-park review can interrogate the reason instead of the prose.</b> Today's review
/// asks only whether the <i>channel</i> can send, so a row parked because the cabinet's entitlement lapsed passes
/// all of its checks and is released on the next tick — FR-8's named gap. Recovering the reason by matching a
/// French sentence is the defect this repo deleted in <c>adoption-gaps-remediation</c>.</para>
///
/// <para>The first three name what the two dispatchers already park for; <see cref="SubscriptionExpired"/> is
/// consumed in Part G. The column lands with Part A's migration so the model and the schema agree in one step.</para>
/// </summary>
public enum OutboxBlockReason
{
    /// <summary>No sender is implemented for the row's channel.</summary>
    ChannelUnsupported = 1,

    /// <summary>The channel exists and the clinic has switched it off.</summary>
    ChannelDisabled = 2,

    /// <summary>The channel is on but has no credentials, endpoint or sender identity.</summary>
    ChannelUnconfigured = 3,

    /// <summary>The cabinet's entitlement has ended or been suspended, so it may not record new work (FR-8).</summary>
    SubscriptionExpired = 4
}
