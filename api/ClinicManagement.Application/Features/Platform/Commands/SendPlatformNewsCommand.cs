using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Commands;

/// <summary>
/// Records a news broadcast with its recipients fixed now; the controller then queues the background send.
/// A repeated <see cref="RequestId"/> answers the first broadcast — the unique index, not a read, guarantees one.
/// </summary>
public class SendPlatformNewsCommand : IRequest<Result<PlatformNewsQueuedDto>>
{
    public Guid RequestId { get; set; }
    public string? Subject { get; set; }
    public IReadOnlyList<PlatformNewsItemRequest>? Items { get; set; }
}

public class SendPlatformNewsCommandHandler
    : IRequestHandler<SendPlatformNewsCommand, Result<PlatformNewsQueuedDto>>
{
    private readonly IPlatformNewsRepository _news;
    private readonly IClinicActivityRepository _activity;
    private readonly IPlatformAccountRepository _accounts;
    private readonly IPlatformSessionContext _session;
    private readonly ITransactionalEmailSender _email;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<SendPlatformNewsCommandHandler> _logger;

    public SendPlatformNewsCommandHandler(
        IPlatformNewsRepository news,
        IClinicActivityRepository activity,
        IPlatformAccountRepository accounts,
        IPlatformSessionContext session,
        ITransactionalEmailSender email,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        ILogger<SendPlatformNewsCommandHandler> logger)
    {
        _news = news;
        _activity = activity;
        _accounts = accounts;
        _session = session;
        _email = email;
        _unitOfWork = unitOfWork;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformNewsQueuedDto>> Handle(
        SendPlatformNewsCommand request, CancellationToken cancellationToken)
    {
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        if (request.RequestId == Guid.Empty)
        {
            return Result<PlatformNewsQueuedDto>.Failure("Envoi non identifié. Rechargez la page.", PlatformNewsRules.InvalidCode);
        }

        try
        {
            if (await _news.GetByRequestIdAsync(request.RequestId, cancellationToken) is { } already)
            {
                return Replay(already);
            }

            if (!_email.IsConfigured)
            {
                return Result<PlatformNewsQueuedDto>.Failure(
                    PlatformNewsRules.EmailNotConfigured, PlatformNewsRules.EmailNotConfiguredCode);
            }

            var drafted = PlatformNewsRules.Normalize(request.Subject, request.Items);
            if (drafted.IsFailure)
            {
                return Result<PlatformNewsQueuedDto>.FailureFrom(drafted);
            }

            var audience = await _activity.GetNewsAudienceAsync(ClinicClock.ClinicToday(), cancellationToken);
            var plan = PlatformNewsAudiencePlan.From(audience);
            if (plan.Recipients.Count == 0)
            {
                return Result<PlatformNewsQueuedDto>.Failure(PlatformNewsRules.NoAudience, PlatformNewsRules.NoAudienceCode);
            }

            var accountId = PlatformAccessLedger.RequireAccountId(_session);
            var account = await _accounts.GetByIdAsync(accountId, cancellationToken);
            var draft = drafted.Value!;

            var broadcast = PlatformNewsBroadcast.Create(
                request.RequestId,
                draft.Subject,
                draft.Items,
                plan.Recipients,
                accountId,
                account?.FullName ?? _session.GetEmail() ?? "Console",
                DateTime.UtcNow);

            await _news.AddAsync(broadcast, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Console account {AccountId} queued news broadcast {BroadcastId} to {Recipients} address(es) in {Clinics} cabinet(s)",
                accountId, broadcast.Id, plan.Recipients.Count, plan.Clinics);

            return Result<PlatformNewsQueuedDto>.Success(new PlatformNewsQueuedDto(
                broadcast.Id, plan.Clinics, plan.Recipients.Count, AlreadyQueued: false, InProgress: true));
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two presses raced past the read; the unique RequestId refused the second — answer the first.
            _logger.LogInformation(ex, "A repeated news send lost the race on {RequestId}; replaying the first", request.RequestId);
            var winner = await _news.GetByRequestIdAsync(request.RequestId, cancellationToken);
            return winner is null
                ? Result<PlatformNewsQueuedDto>.Failure("Erreur lors de l'envoi des nouveautés.", ex)
                : Replay(winner);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error queueing a news broadcast");
            return Result<PlatformNewsQueuedDto>.Failure("Erreur lors de l'envoi des nouveautés.", ex);
        }
    }

    private static Result<PlatformNewsQueuedDto> Replay(PlatformNewsBroadcast broadcast) =>
        Result<PlatformNewsQueuedDto>.Success(new PlatformNewsQueuedDto(
            broadcast.Id,
            broadcast.Recipients.Select(r => r.ClinicId).Distinct().Count(),
            broadcast.Recipients.Count,
            AlreadyQueued: true,
            InProgress: broadcast.CompletedAtUtc is null));

    /// <summary>PostgreSQL 23505 — matched on the type name, following <c>UnitOfWork.IsExclusionViolation</c>.</summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
        {
            if (inner.GetType().FullName == "Npgsql.PostgresException"
                && inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505")
            {
                return true;
            }
        }

        return false;
    }
}
