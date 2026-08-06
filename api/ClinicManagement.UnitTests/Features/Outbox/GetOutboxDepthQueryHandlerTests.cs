using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Outbox.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Outbox;

/// <summary>
/// The operator's queue-depth read (multi-tenant-cloud US-6).
///
/// <para><b>Two things here are worth a test rather than a comment.</b> First, all three queues must be measured
/// against <b>one</b> instant: the whole value of the read is comparing the figures with each other and with the
/// reading taken five minutes ago, and three clock reads would make « due » mean three slightly different things.
/// Second, every read must be issued with the caller's <i>own</i> clinic id — the reminder outbox carries no query
/// filter at all (it is drained cross-clinic by the dispatcher), so the handler's argument is the only thing
/// keeping one practice's queue out of another's report.</para>
/// </summary>
public class GetOutboxDepthQueryHandlerTests
{
    private static readonly Guid Clinic = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherClinic = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string CallerId = "local|admin";

    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IDocumentEmailRepository> _documentEmails = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _clinicContext = new();

    private GetOutboxDepthQueryHandler Handler() => new(
        _notifications.Object,
        _invoices.Object,
        _documentEmails.Object,
        _users.Object,
        _clinicContext.Object,
        NullLogger<GetOutboxDepthQueryHandler>.Instance);

    private void GivenCaller(string role, Guid clinicId)
    {
        var user = User.CreateLocalUser(clinicId, role, "admin@cabinet.tn", "HASH", "Amel Ben Salah");
        _clinicContext.Setup(c => c.GetUserId()).Returns(CallerId);
        _users.Setup(u => u.GetByAuth0SubAsync(CallerId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
    }

    private void GivenQueues(
        ReminderOutboxDepth? reminders = null,
        EInvoiceOutboxDepth? eInvoices = null,
        DocumentEmailOutboxDepth? documentEmails = null)
    {
        _notifications
            .Setup(r => r.GetOutboxDepthAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reminders ?? new ReminderOutboxDepth(0, 0, 0, 0, null));

        _invoices
            .Setup(r => r.GetEInvoiceOutboxDepthAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(eInvoices ?? new EInvoiceOutboxDepth(0, 0, 0, null));

        _documentEmails
            .Setup(r => r.GetOutboxDepthAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentEmails ?? new DocumentEmailOutboxDepth(0, 0, 0, null));
    }

    [Fact]
    public async Task Every_figure_reaches_the_dto_unchanged()
    {
        GivenCaller("admin", Clinic);

        var oldestReminder = new DateTime(2026, 8, 5, 6, 30, 0, DateTimeKind.Utc);
        var oldestAttempt = new DateTime(2026, 8, 5, 7, 0, 0, DateTimeKind.Utc);
        var oldestEmail = new DateTime(2026, 8, 4, 18, 0, 0, DateTimeKind.Utc);

        GivenQueues(
            new ReminderOutboxDepth(Pending: 40, Due: 12, Blocked: 3, FailedRecent: 2, oldestReminder),
            new EInvoiceOutboxDepth(Queued: 7, Due: 4, Failed: 1, oldestAttempt),
            new DocumentEmailOutboxDepth(Queued: 5, Blocked: 4, Failed: 6, oldestEmail));

        var result = await Handler().Handle(new GetOutboxDepthQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;

        Assert.Equal(40, dto.Reminders.Pending);
        Assert.Equal(12, dto.Reminders.Due);
        Assert.Equal(3, dto.Reminders.Blocked);
        Assert.Equal(2, dto.Reminders.FailedRecent);
        Assert.Equal(oldestReminder, dto.Reminders.OldestDueScheduledForUtc);

        Assert.Equal(7, dto.EInvoices.Queued);
        Assert.Equal(4, dto.EInvoices.Due);
        Assert.Equal(1, dto.EInvoices.Failed);
        Assert.Equal(oldestAttempt, dto.EInvoices.OldestDueNextAttemptUtc);

        Assert.Equal(5, dto.DocumentEmails.Queued);
        // Blocked is what separates « the queue is deep » from « the dispatcher is dead » on this queue, which had
        // no such figure until the review's finding 5.
        Assert.Equal(4, dto.DocumentEmails.Blocked);
        Assert.Equal(6, dto.DocumentEmails.Failed);
        Assert.Equal(oldestEmail, dto.DocumentEmails.OldestQueuedUtc);
    }

    [Fact]
    public async Task All_three_queues_are_measured_against_one_instant()
    {
        GivenCaller("admin", Clinic);
        GivenQueues();

        DateTime reminderNow = default, invoiceNow = default;

        _notifications
            .Setup(r => r.GetOutboxDepthAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, DateTime now, DateTime _, CancellationToken _) => reminderNow = now)
            .ReturnsAsync(new ReminderOutboxDepth(0, 0, 0, 0, null));

        _invoices
            .Setup(r => r.GetEInvoiceOutboxDepthAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, DateTime now, CancellationToken _) => invoiceNow = now)
            .ReturnsAsync(new EInvoiceOutboxDepth(0, 0, 0, null));

        var result = await Handler().Handle(new GetOutboxDepthQuery(), CancellationToken.None);

        Assert.Equal(reminderNow, invoiceNow);
        Assert.Equal(reminderNow, result.Value!.MeasuredAtUtc);
    }

    [Fact]
    public async Task The_failed_window_is_reported_with_the_figure_it_bounds()
    {
        GivenCaller("admin", Clinic);
        GivenQueues();

        DateTime failedSince = default;
        _notifications
            .Setup(r => r.GetOutboxDepthAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, DateTime _, DateTime since, CancellationToken _) => failedSince = since)
            .ReturnsAsync(new ReminderOutboxDepth(0, 0, 0, 0, null));

        var result = await Handler().Handle(new GetOutboxDepthQuery(), CancellationToken.None);

        // A bare « 4 failed » cannot say whether that is today or since the install, so the window travels with it
        // — and it must be the same bound the repository was actually given.
        Assert.Equal(failedSince, result.Value!.Reminders.FailedSinceUtc);
        Assert.Equal(
            GetOutboxDepthQuery.FailedWindowDays,
            (int)Math.Round((result.Value.MeasuredAtUtc - failedSince).TotalDays));
    }

    [Fact]
    public async Task Every_read_is_issued_with_the_callers_own_clinic()
    {
        GivenCaller("admin", Clinic);
        GivenQueues();

        await Handler().Handle(new GetOutboxDepthQuery(), CancellationToken.None);

        _notifications.Verify(r => r.GetOutboxDepthAsync(
            Clinic, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _invoices.Verify(r => r.GetEInvoiceOutboxDepthAsync(
            Clinic, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _documentEmails.Verify(r => r.GetOutboxDepthAsync(Clinic, It.IsAny<CancellationToken>()), Times.Once);

        // The reminder outbox carries no query filter, so the argument is the ONLY isolation there is.
        _notifications.Verify(r => r.GetOutboxDepthAsync(
            OtherClinic, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("doctor")]
    [InlineData("secretary")]
    public async Task A_non_admin_is_refused(string role)
    {
        GivenCaller(role, Clinic);
        GivenQueues();

        var result = await Handler().Handle(new GetOutboxDepthQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("administrateurs", result.Error, StringComparison.Ordinal);

        // And nothing was read — a refusal that still queried would leak timing and load for no reason.
        _notifications.VerifyNoOtherCalls();
        _invoices.VerifyNoOtherCalls();
        _documentEmails.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task No_session_is_refused_in_french()
    {
        _clinicContext.Setup(c => c.GetUserId()).Returns((string?)null);

        var result = await Handler().Handle(new GetOutboxDepthQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("Session invalide", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_caller_is_refused_in_french()
    {
        _clinicContext.Setup(c => c.GetUserId()).Returns(CallerId);
        _users.Setup(u => u.GetByAuth0SubAsync(CallerId, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await Handler().Handle(new GetOutboxDepthQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("introuvable", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_repository_failure_becomes_a_french_business_error_and_not_a_stack_trace()
    {
        GivenCaller("admin", Clinic);
        GivenQueues();
        _invoices
            .Setup(r => r.GetEInvoiceOutboxDepthAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("relation \"Invoices\" does not exist"));

        var result = await Handler().Handle(new GetOutboxDepthQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain("relation", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
