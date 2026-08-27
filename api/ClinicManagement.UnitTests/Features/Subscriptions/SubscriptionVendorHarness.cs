using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;

namespace ClinicManagement.UnitTests.Features.Subscriptions;

/// <summary>
/// The fixture the three vendor-command classes share (<c>clinic-subscription</c> Part F).
///
/// <para><b>An in-memory ledger rather than a mocked repository</b>, on <c>SubscriptionWarningTests</c>' precedent:
/// every assertion here is about what the <i>ledger</i> ends up holding and what the fold makes of it — entries
/// accumulating (AC-5.3), a cancelled row staying (AC-5.5), a date moving because a middle entry went (AC-5.4). A
/// mock would prove a method was called and nothing about any of that.</para>
///
/// <para>⚠️ Every member the vendor commands do not use <b>throws</b>, deliberately: a fake that quietly answers an
/// unrelated read lets a wrong implementation pass by taking another path.</para>
/// </summary>
internal sealed class FakeSubscriptionRepository : IClinicSubscriptionRepository
{
    private readonly List<SubscriptionPeriod> _entries = new();

    public ClinicSubscription? Subscription { get; set; }

    /// <summary>Rows for <c>GetForReportAsync</c>; the vendor report is the only caller.</summary>
    public List<ClinicSubscriptionReportRow> ReportRows { get; } = new();

    /// <summary>What a fresh read would return — set to simulate another writer having committed.</summary>
    public ClinicSubscription? ReloadsAs { get; set; }

    public int Reloads { get; private set; }

    public IReadOnlyList<SubscriptionPeriod> Entries => _entries;

    public Task<ClinicSubscription?> GetByClinicAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        if (Subscription is not null && Subscription.ClinicId != clinicId)
        {
            return Task.FromResult<ClinicSubscription?>(null);
        }

        if (Reloads++ > 0 && ReloadsAs is not null)
        {
            Subscription = ReloadsAs;
        }

        return Task.FromResult(Subscription);
    }

    public Task<IReadOnlyList<SubscriptionPeriod>> GetEntriesAsync(
        Guid clinicId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionPeriod>>(_entries
            .Where(e => e.ClinicId == clinicId)
            .OrderBy(e => e.RecordedAtUtc)
            .ThenBy(e => e.Id)
            .ToList());

    /// <summary>
    /// The vendor's takings over a window — non-cancelled entries only, and an entry with no amount contributes
    /// nothing rather than being skipped. The predicate is the repository's, so a test can hold the console's
    /// summary equal to what the ledger actually holds.
    /// </summary>
    public Task<decimal> GetVendorCollectedBetweenAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries
            .Where(e => !e.IsCancelled && e.RecordedAtUtc >= fromUtc && e.RecordedAtUtc <= toUtc)
            .Sum(e => e.Amount ?? 0m));

    public Task<IReadOnlyList<ClinicSubscriptionReportRow>> GetForReportAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ClinicSubscriptionReportRow>>(ReportRows);

    /// <summary>Served from the same rows, so a test cannot make the two reads disagree by populating only one.</summary>
    public Task<ClinicSubscriptionReportRow?> GetReportRowAsync(
        Guid clinicId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReportRows.FirstOrDefault(r => r.ClinicId == clinicId));

    public Task AddEntryAsync(SubscriptionPeriod entry, CancellationToken cancellationToken = default)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ClinicSubscription subscription, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task AddAsync(ClinicSubscription subscription, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The vendor commands never create an entitlement.");

    /// <summary>Seeds an already-committed entry, as provisioning or an earlier grant would have left it.</summary>
    public SubscriptionPeriod Seed(SubscriptionPeriod entry)
    {
        _entries.Add(entry);
        return entry;
    }
}

/// <summary>The two lookups the vendor commands share, plus the unit of work they save through.</summary>
internal sealed class SubscriptionVendorHarness
{
    public static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public const string AdminEmail = "owner@cabinet.tn";

    public FakeSubscriptionRepository Subscriptions { get; } = new();

    public Mock<IClinicRepository> Clinics { get; } = new();

    public Mock<IUserRepository> Users { get; } = new();

    public Mock<IUnitOfWork> UnitOfWork { get; } = new();

    public int Saves { get; private set; }

    public SubscriptionVendorHarness()
    {
        Clinics.Setup(c => c.ExistsAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Clinics.Setup(c => c.ExistsAsync(OtherClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Users.Setup(u => u.GetByEmailAsync(AdminEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(User.CreateLocalUser(ClinicId, "admin", AdminEmail, "hash", "Dr Ben Salah"));

        UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++Saves);
    }

    /// <summary>An entitlement whose ledger already holds one grant, folded — the ordinary starting state.</summary>
    public ClinicSubscription GivenEntitlement(
        DateTime recordedOn, int? durationMonths = null, int? durationDays = null, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var subscription = ClinicSubscription.For(ClinicId, now);

        var opening = SubscriptionPeriod.Create(
            ClinicId, SubscriptionPeriodKind.Paid, recordedOn, now,
            durationMonths: durationMonths, durationDays: durationDays);

        Subscriptions.Seed(opening);
        subscription.RecomputeFrom(new[] { opening }, DateTime.UtcNow);
        Subscriptions.Subscription = subscription;
        return subscription;
    }
}
