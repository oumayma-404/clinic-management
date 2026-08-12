namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// The operator-configured half of the WhatsApp reminder forfait: how many messages a new cabinet starts with each
/// month, and who it contacts when it runs out (AC-2.7).
///
/// <para><see cref="ISubscriptionPricing"/>'s precedent — a commercial figure must not be compiled in. R-12: the
/// default is a commercial decision that has not been made, so it ships as a <b>provisional</b> operator setting
/// and nothing in the code depends on its value.</para>
///
/// <para>⚠️ Both contact members may be <b>absent</b>, and absent means the screen renders <b>no contact route at
/// all</b> rather than an empty one (AC-2.7) — the same rule an unpublished price follows.</para>
/// </summary>
public interface IMessagingAllowancePolicy
{
    /// <summary>
    /// The standing monthly allowance a cabinet is provisioned with, and the figure the rollout backfill wrote
    /// (FR-3). Changing it later moves <b>no</b> existing cabinet's allowance: each is recorded as a ledger entry
    /// carrying its own figure, so a cabinet's forfait is fixed by what was recorded rather than by what the
    /// setting says today.
    /// </summary>
    int DefaultMessagesPerMonth { get; }

    /// <summary>Where an exhausted cabinet writes to ask for more, or null where the operator has published none.</summary>
    string? ContactEmail { get; }

    /// <summary>Where an exhausted cabinet calls, or null where the operator has published none.</summary>
    string? ContactPhone { get; }
}
