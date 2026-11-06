using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform;
using Hangfire;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// Sends the console's news e-mails: <see cref="Dispatch"/> right after a send is recorded, <see cref="ResumeUnfinished"/>
/// every few minutes for any send a crash or exhausted retries left unfinished. Recipient claims keep both safe together.
/// </summary>
public class PlatformNewsDispatchJob
{
    public const string ResumeJobId = "resume-platform-news";

    private readonly IPlatformNewsDispatcher _dispatcher;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;

    public PlatformNewsDispatchJob(
        IPlatformNewsDispatcher dispatcher, IAuditActorProvider auditActor, ITenantScope tenantScope)
    {
        _dispatcher = dispatcher;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task Dispatch(Guid broadcastId, CancellationToken cancellationToken)
    {
        Declare();
        await _dispatcher.DispatchAsync(broadcastId, cancellationToken);
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ResumeUnfinished(CancellationToken cancellationToken)
    {
        Declare();
        await _dispatcher.ResumeUnfinishedAsync(cancellationToken);
    }

    private void Declare()
    {
        _auditActor.RunAs(nameof(PlatformNewsDispatchJob));
        _tenantScope.UseSystemWide("PlatformNewsDispatchJob sends the vendor's news to every eligible cabinet");
    }
}
