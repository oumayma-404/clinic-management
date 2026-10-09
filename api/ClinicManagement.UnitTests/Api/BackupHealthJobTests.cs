using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.API.Controllers.Platform;
using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Queries;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Deployment;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// [server-loss-recovery Part 3] The morning alert. Most cases are about who is told and when NOT to send —
/// an alarm that fires on healthy mornings is an alarm the vendor learns to ignore, and one that silently goes
/// nowhere is the 33 silent nights again.
/// </summary>
public class BackupHealthJobTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 6, 0, 0, DateTimeKind.Utc);

    private static readonly HostBackupSnapshot Healthy = new(
        new NightlyBackupRecord("succeeded", null, Now.AddHours(-4), Now.AddHours(-4)),
        new WalArchiveRecord(900, Now.AddMinutes(-2), 0, null));

    private static readonly HostBackupSnapshot NightlyFailing = new(
        new NightlyBackupRecord("failed", "objects", Now.AddHours(-4), Now.AddDays(-34)),
        new WalArchiveRecord(900, Now.AddMinutes(-2), 0, null));

    private sealed class Harness
    {
        public Mock<IHostBackupStatusReader> Reader { get; } = new();
        public Mock<IPlatformAccountRepository> Accounts { get; } = new();
        public Mock<ITransactionalEmailSender> Email { get; } = new();
        public Mock<ITenantScope> Scope { get; } = new();
        public Mock<IAuditActorProvider> Actor { get; } = new();
        public List<(string To, string Subject, EmailContent Content)> Sent { get; } = new();
        public Dictionary<string, string?> Config { get; } = new();

        public Harness(HostBackupSnapshot snapshot)
        {
            Reader.Setup(r => r.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
            Accounts.Setup(a => a.GetActiveEmailsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { "vendeur@apexa.tn", "associe@apexa.tn" });
            Email.SetupGet(e => e.IsConfigured).Returns(true);
            Email.Setup(e => e.SendAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, EmailContent, CancellationToken>((to, subject, content, _) =>
                    Sent.Add((to, subject, content)))
                .ReturnsAsync(TransactionalEmailResult.Sent);
        }

        public BackupHealthJob Job(DeploymentKind kind = DeploymentKind.HostedMultiTenant) => new(
            Reader.Object,
            Accounts.Object,
            Email.Object,
            DeploymentProfile.For(kind),
            new ConfigurationBuilder().AddInMemoryCollection(Config).Build(),
            Actor.Object,
            Scope.Object,
            NullLogger<BackupHealthJob>.Instance);
    }

    [Fact]
    public async Task A_Healthy_Morning_Sends_Nothing()
    {
        var h = new Harness(Healthy);

        await h.Job().CheckBackupHealth(Now, CancellationToken.None);

        Assert.Empty(h.Sent);
    }

    [Fact]
    public async Task A_Failing_Copy_Is_Emailed_To_Every_Active_Console_Account_When_Nobody_Is_Named()
    {
        var h = new Harness(NightlyFailing);

        await h.Job().CheckBackupHealth(Now, CancellationToken.None);

        Assert.Equal(new[] { "vendeur@apexa.tn", "associe@apexa.tn" }, h.Sent.Select(s => s.To));
        Assert.All(h.Sent, s => Assert.Equal("Sauvegarde hors serveur EN ÉCHEC", s.Subject));
    }

    [Fact]
    public async Task A_Named_Recipient_Replaces_The_Console_Accounts()
    {
        var h = new Harness(NightlyFailing);
        h.Config[BackupHealthJob.AlertEmailKey] = " ops@apexa.tn , ";

        await h.Job().CheckBackupHealth(Now, CancellationToken.None);

        Assert.Equal(new[] { "ops@apexa.tn" }, h.Sent.Select(s => s.To));
        h.Accounts.Verify(a => a.GetActiveEmailsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // No record at all is « En retard », and it is still worth a message — it is what an unwired deployment looks like.
    [Fact]
    public async Task No_Record_At_All_Is_Reported_As_Late()
    {
        var h = new Harness(new HostBackupSnapshot(null, null));

        await h.Job().CheckBackupHealth(Now, CancellationToken.None);

        Assert.NotEmpty(h.Sent);
        Assert.All(h.Sent, s => Assert.Equal("Sauvegarde hors serveur EN RETARD", s.Subject));
    }

    [Fact]
    public async Task The_Message_Names_The_Stage_And_The_Last_Success()
    {
        var h = new Harness(NightlyFailing);

        await h.Job().CheckBackupHealth(Now, CancellationToken.None);

        var nightly = h.Sent[0].Content.Details.Single(d => d.Label.StartsWith("Copie nocturne", StringComparison.Ordinal));
        Assert.Contains("« objects »", nightly.Value);
        // Now − 34 days = 2026-09-03 06:00 UTC, which is 07:00 in Tunis — the e-mail speaks the clinic's clock.
        Assert.Contains("03/09/2026 à 07:00", nightly.Value);
    }

    [Fact]
    public async Task Without_Smtp_Nothing_Is_Attempted()
    {
        var h = new Harness(NightlyFailing);
        h.Email.SetupGet(e => e.IsConfigured).Returns(false);

        await h.Job().CheckBackupHealth(Now, CancellationToken.None);

        Assert.Empty(h.Sent);
    }

    [Fact]
    public async Task On_A_Clinic_Pc_The_Job_Does_Nothing_At_All()
    {
        var h = new Harness(NightlyFailing);

        await h.Job(DeploymentKind.SelfHostedLan).CheckBackupHealth(Now, CancellationToken.None);

        Assert.Empty(h.Sent);
        h.Reader.Verify(r => r.ReadAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task It_Declares_Its_Scope_And_Its_Actor_Before_Reading()
    {
        var h = new Harness(Healthy);

        await h.Job().CheckBackupHealth(Now, CancellationToken.None);

        h.Scope.Verify(s => s.UseSystemWide(It.IsAny<string>()), Times.Once);
        h.Actor.Verify(a => a.RunAs(nameof(BackupHealthJob)), Times.Once);
    }

    // One failed delivery must not stop the next recipient from being told.
    [Fact]
    public async Task A_Failed_Delivery_Does_Not_Stop_The_Others()
    {
        var h = new Harness(NightlyFailing);
        h.Email.Setup(e => e.SendAsync(
                "vendeur@apexa.tn", It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.Failed("relay refused"));

        await h.Job().CheckBackupHealth(Now, CancellationToken.None);

        h.Email.Verify(e => e.SendAsync(
            "associe@apexa.tn", It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── The console endpoint ──────────────────────────────────────────────────────────────────────────

    // Absent rather than present-and-empty: a clinic PC must never be told its backups are « en retard ».
    [Fact]
    public async Task The_Console_Read_Is_404_Before_The_Mediator_Where_Nothing_Is_Watched()
    {
        var mediator = new Mock<IMediator>();
        var controller = new PlatformBackupHealthController(
            mediator.Object, DeploymentProfile.For(DeploymentKind.SelfHostedLan));

        var result = await controller.GetBackupHealth(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Empty(mediator.Invocations);
    }

    [Fact]
    public async Task The_Console_Read_Answers_On_The_Hosted_Deployment()
    {
        var reader = new Mock<IHostBackupStatusReader>();
        reader.Setup(r => r.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(NightlyFailing);
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetPlatformBackupHealthQuery>(), It.IsAny<CancellationToken>()))
            .Returns((GetPlatformBackupHealthQuery q, CancellationToken ct) =>
                new GetPlatformBackupHealthQueryHandler(reader.Object).Handle(q, ct));
        var controller = new PlatformBackupHealthController(
            mediator.Object, DeploymentProfile.For(DeploymentKind.HostedMultiTenant));

        var result = await controller.GetBackupHealth(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ClinicManagement.Application.Features.Platform.Dtos.PlatformBackupHealthDto>(ok.Value);
        Assert.Equal(BackupHealthRules.Failing, dto.Verdict);
    }
}
