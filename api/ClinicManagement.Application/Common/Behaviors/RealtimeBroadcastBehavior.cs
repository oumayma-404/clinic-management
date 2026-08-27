using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Common.Behaviors;

/// <summary>
/// Cross-cutting real-time broadcast. After ANY mutating command (a request whose namespace is
/// <c>...Features.&lt;Area&gt;.Commands</c>) completes successfully, this signals the caller's clinic that
/// <c>&lt;Area&gt;</c> changed, so connected clients refetch. It is the single wiring point that makes
/// "any edit is live" — command handlers need no per-command broadcast code, and new commands are
/// covered automatically by convention.
///
/// Additive / fail-safe:
/// - Runs strictly AFTER the handler returns (i.e. after the handler's <c>SaveChangesAsync</c> commit),
///   so a rolled-back / failed command never broadcasts (its response is a failure <see cref="Result"/>).
/// - Never affects the request outcome: the notifier swallows its own transport failures, and this
///   behavior additionally swallows any resolution failure and always returns the handler's response.
///
/// Queries (which return non-<see cref="Result"/> or live outside a <c>.Commands</c> namespace) and a
/// short list of non-data command areas (auth / AI chat / backup) are skipped — they are not clinic
/// data any list view mirrors, so they must not emit a spurious refetch signal.
/// </summary>
public class RealtimeBroadcastBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // Resolved once per closed generic type (TRequest is fixed per behavior instantiation).
    private static readonly string? Resource = RealtimeResourceResolver.Resolve(typeof(TRequest));

    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<RealtimeBroadcastBehavior<TRequest, TResponse>> _logger;

    public RealtimeBroadcastBehavior(
        IRealtimeNotifier realtimeNotifier,
        IClinicContext clinicContext,
        IUserRepository userRepository,
        ILogger<RealtimeBroadcastBehavior<TRequest, TResponse>> logger)
    {
        _realtimeNotifier = realtimeNotifier;
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();

        // Only broadcast for a broadcastable command that actually succeeded. A broadcast (or the
        // clinic lookup it needs) must never surface as a failure of the committed command.
        if (Resource != null && response is Result { IsSuccess: true })
        {
            try
            {
                var clinicId = await ResolveClinicIdAsync(cancellationToken);
                if (clinicId.HasValue)
                {
                    await _realtimeNotifier.NotifyEntityChangedAsync(clinicId.Value, Resource, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Real-time broadcast skipped for {Request}", typeof(TRequest).Name);
            }
        }

        return response;
    }

    /// <summary>
    /// Resolves the caller's clinic id the same way handlers do — user id from the JWT via
    /// <see cref="IClinicContext"/>, then the clinic from the user record. Returns null when there is
    /// no authenticated user (e.g. first-run setup), in which case nothing is broadcast.
    /// </summary>
    private async Task<Guid?> ResolveClinicIdAsync(CancellationToken cancellationToken)
    {
        var userId = _clinicContext.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
        return user?.ClinicId;
    }
}
