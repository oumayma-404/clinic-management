using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The three dependencies <c>GetPlatformClinicDetailQueryHandler</c> grew when the cabinet file gained its WhatsApp
/// reminder section (<c>vendor-whatsapp-messaging-quota</c> Part 3, AC-8.1), stubbed so the three <b>pre-existing</b>
/// detail-read test classes stay byte-for-byte the reads they were.
///
/// <para><b>⚠️ <see cref="NotSold"/> answers <c>false</c>, and that is the point.</b> Those classes are about the
/// subscription ledger, a suspension and the access journal; with the capability off the messaging section is <c>null</c>
/// and neither repository below is touched, so none of their assertions can be satisfied — or broken — by a section they
/// were never written about. The messaging section's own coverage is <c>PlatformMessagingReadTests</c>.</para>
///
/// <para>⚠️ Shared rather than three mocks per file, for the ordinary reason: the next constructor argument would
/// otherwise be a four-way edit, which is how one of the four ends up stubbing something subtly different.</para>
/// </summary>
internal static class PlatformMessagingReadStubs
{
    /// <summary>A deployment that does not sell vendor messaging — every surface of the feature absent (EC-16).</summary>
    public static IVendorMessagingAvailability NotSold()
    {
        var availability = new Mock<IVendorMessagingAvailability>();
        availability.SetupGet(a => a.SellsVendorMessaging).Returns(false);
        availability.SetupGet(a => a.CanOnboardCabinets).Returns(false);
        return availability.Object;
    }

    /// <summary>
    /// An allowance repository with an empty ledger.
    ///
    /// <para>⚠️ Stubbed to return an empty list rather than left unconfigured even though the capability above means it
    /// is never called: Moq's default for a collection-returning read is <b>null</b>, and this codebase's
    /// catch-all-to-<c>Result.Failure</c> convention would turn the resulting <c>NullReferenceException</c> into a
    /// French business error nowhere near its cause — the gotcha the test guide names.</para>
    /// </summary>
    public static IMessagingAllowanceRepository NoAllowances()
    {
        var allowances = new Mock<IMessagingAllowanceRepository>();
        allowances
            .Setup(r => r.GetEntriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MessagingAllowanceEntry>());
        allowances
            .Setup(r => r.GetMonthAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicMessagingMonth?)null);
        return allowances.Object;
    }

    /// <summary>A cabinet with no reminder settings row — the state a fresh cabinet is genuinely in.</summary>
    public static IClinicReminderSettingsRepository NoReminderSettings()
    {
        var settings = new Mock<IClinicReminderSettingsRepository>();
        settings
            .Setup(r => r.GetByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicReminderSettings?)null);
        return settings.Object;
    }
}
