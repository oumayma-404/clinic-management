using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;

namespace ClinicManagement.UnitTests.Features.Messaging;

/// <summary>
/// An in-memory allocation ledger and its counting rows (<c>vendor-whatsapp-messaging-quota</c> Part 3).
///
/// <para><b>A real ledger rather than a mocked repository</b>, on <c>FakeSubscriptionRepository</c>'s precedent: every
/// assertion in Part 3 is about what the ledger ends up <i>holding</i> and what the fold makes of it — entries
/// accumulating (AC-6.2), a cancelled row staying (AC-7.2), and every month it fed recomputing including the current one
/// (AC-7.4). A mock would prove a method was called and nothing about any of that.</para>
///
/// <para>⚠️ Every member the Part 3 commands do not use <b>throws</b>, deliberately: a fake that quietly answers an
/// unrelated read lets a wrong implementation pass by taking another path.</para>
/// </summary>
internal sealed class FakeMessagingAllowanceRepository : IMessagingAllowanceRepository
{
    private readonly List<MessagingAllowanceEntry> _entries = new();
    private readonly List<ClinicMessagingMonth> _months = new();

    /// <summary>Rows for <c>GetForReportAsync</c>; the report verb is the only caller.</summary>
    public List<ClinicMessagingReportRow> ReportRows { get; } = new();

    public IReadOnlyList<MessagingAllowanceEntry> Entries => _entries;

    public IReadOnlyList<ClinicMessagingMonth> Months => _months;

    public Task<IReadOnlyList<MessagingAllowanceEntry>> GetEntriesAsync(
        Guid clinicId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MessagingAllowanceEntry>>(_entries
            .Where(e => e.ClinicId == clinicId)
            .OrderBy(e => e.EffectiveMonth, StringComparer.Ordinal)
            .ThenBy(e => e.RecordedAtUtc)
            .ThenBy(e => e.Id)
            .ToList());

    public Task<MessagingAllowanceEntry?> GetEntryAsync(
        Guid clinicId, Guid entryId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.FirstOrDefault(e => e.ClinicId == clinicId && e.Id == entryId));

    public Task<ClinicMessagingMonth?> GetMonthAsync(
        Guid clinicId, string monthKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_months.FirstOrDefault(m => m.ClinicId == clinicId && m.MonthKey == monthKey));

    public Task<IReadOnlyList<ClinicMessagingMonth>> GetMonthsAsync(
        Guid clinicId, string fromMonthKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ClinicMessagingMonth>>(_months
            .Where(m => m.ClinicId == clinicId
                        && string.CompareOrdinal(m.MonthKey, fromMonthKey) >= 0)
            .OrderBy(m => m.MonthKey, StringComparer.Ordinal)
            .ToList());

    public Task<IReadOnlyList<ClinicMessagingReportRow>> GetForReportAsync(
        string monthKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ClinicMessagingReportRow>>(ReportRows);

    public Task AddEntryAsync(MessagingAllowanceEntry entry, CancellationToken cancellationToken = default)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task AddMonthAsync(ClinicMessagingMonth month, CancellationToken cancellationToken = default)
    {
        _months.Add(month);
        return Task.CompletedTask;
    }

    public Task UpdateEntryAsync(MessagingAllowanceEntry entry, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task UpdateMonthAsync(ClinicMessagingMonth month, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>Seeds an already-committed allocation, as provisioning or an earlier grant would have left it.</summary>
    public MessagingAllowanceEntry Seed(MessagingAllowanceEntry entry)
    {
        _entries.Add(entry);
        return entry;
    }

    /// <summary>Seeds a counting row, as the daily provisioning pass would have.</summary>
    public ClinicMessagingMonth SeedMonth(ClinicMessagingMonth month)
    {
        _months.Add(month);
        return month;
    }
}

/// <summary>The lookups and the unit of work the Part 3 commands share.</summary>
internal sealed class MessagingVendorHarness
{
    public static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public const string ClinicName = "Cabinet Ben Ali";
    public const string AdminEmail = "owner@cabinet.tn";

    public FakeMessagingAllowanceRepository Allowances { get; } = new();

    public Mock<IClinicRepository> Clinics { get; } = new();

    public Mock<IUserRepository> Users { get; } = new();

    public Mock<IUnitOfWork> UnitOfWork { get; } = new();

    public int Saves { get; private set; }

    public MessagingVendorHarness()
    {
        Clinics.Setup(c => c.ExistsAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Clinics.Setup(c => c.ExistsAsync(OtherClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Clinics.Setup(c => c.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicId, ClinicName, city: "Tunis"));

        Users.Setup(u => u.GetByEmailAsync(AdminEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(User.CreateLocalUser(ClinicId, "admin", AdminEmail, "hash", "Dr Ben Salah"));

        UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++Saves);
    }

    /// <summary>
    /// The ordinary starting state: one standing allocation effective <paramref name="effectiveMonth"/>, and that
    /// month's counting row carrying the same figure — what provisioning (FR-3) leaves behind.
    /// </summary>
    public (MessagingAllowanceEntry Entry, ClinicMessagingMonth Month) GivenStanding(
        int messagesPerMonth, string effectiveMonth, int consumed = 0, DateTime? recordedAtUtc = null)
    {
        var now = recordedAtUtc ?? new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

        var entry = Allowances.Seed(
            MessagingAllowanceEntry.Provisioned(ClinicId, messagesPerMonth, effectiveMonth, now));

        var month = Allowances.SeedMonth(
            ClinicMessagingMonth.For(ClinicId, effectiveMonth, messagesPerMonth, now));

        for (var i = 0; i < consumed; i++)
        {
            month.RecordSend(now);
        }

        return (entry, month);
    }
}
