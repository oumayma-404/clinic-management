using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

// Fake feature requests in real ".Features.<Area>.Commands|Queries" namespaces so the behavior's
// structural (namespace-based) resource resolution can be exercised without coupling to any specific
// production command's response type.
namespace ClinicManagement.UnitTests.Fakes.Features.Widgets.Commands
{
    public class CreateWidgetCommand : IRequest<Result> { }
}

namespace ClinicManagement.UnitTests.Fakes.Features.Widgets.Queries
{
    public class GetWidgetsQuery : IRequest<Result> { }
}

namespace ClinicManagement.UnitTests.Fakes.Features.Auth.Commands
{
    public class SignInWidgetCommand : IRequest<Result> { }
}

namespace ClinicManagement.UnitTests.Common.Behaviors
{
    using ClinicManagement.UnitTests.Fakes.Features.Auth.Commands;
    using ClinicManagement.UnitTests.Fakes.Features.Widgets.Commands;
    using ClinicManagement.UnitTests.Fakes.Features.Widgets.Queries;

    /// <summary>
    /// Verifies the cross-cutting real-time broadcast fires exactly when it should: after a successful
    /// mutating command, carrying the command's feature area as the resource key, scoped to the caller's
    /// clinic (AC-1/AC-2). It must NOT fire for failed commands (broadcast only after commit), for
    /// queries, for excluded non-data areas, or when no clinic can be resolved — and a broadcast failure
    /// must never surface to the caller (AC-5, additive).
    /// </summary>
    public class RealtimeBroadcastBehaviorTests
    {
        private static readonly Guid ClinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        private static User NewUser() =>
            User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");

        private static async Task<(TResponse response, Mock<IRealtimeNotifier> notifier)> RunAsync<TRequest, TResponse>(
            TRequest request,
            TResponse response,
            User? resolvedUser,
            Mock<IRealtimeNotifier>? notifierOverride = null)
            where TRequest : IRequest<TResponse>
        {
            var notifier = notifierOverride ?? new Mock<IRealtimeNotifier>();

            var context = new Mock<IClinicContext>();
            context.Setup(c => c.GetUserId()).Returns(resolvedUser?.Id);

            var users = new Mock<IUserRepository>();
            users.Setup(r => r.GetByAuth0SubAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resolvedUser);

            var behavior = new RealtimeBroadcastBehavior<TRequest, TResponse>(
                notifier.Object,
                context.Object,
                users.Object,
                NullLogger<RealtimeBroadcastBehavior<TRequest, TResponse>>.Instance);

            var result = await behavior.Handle(request, _ => Task.FromResult(response), CancellationToken.None);
            return (result, notifier);
        }

        // [AC-1][AC-2] A successful command broadcasts its area to the caller's clinic, and the behavior
        // returns the handler's response unchanged.
        [Fact]
        public async Task Successful_Command_Broadcasts_Area_To_Its_Clinic()
        {
            var response = Result.Success();

            var (returned, notifier) = await RunAsync<CreateWidgetCommand, Result>(
                new CreateWidgetCommand(), response, NewUser());

            Assert.Same(response, returned);
            notifier.Verify(n => n.NotifyEntityChangedAsync(ClinicId, "widgets", It.IsAny<CancellationToken>()), Times.Once);
        }

        // [Edge] A failed command must NOT broadcast (broadcast fires only after a committed change).
        [Fact]
        public async Task Failed_Command_Does_Not_Broadcast()
        {
            var (_, notifier) = await RunAsync<CreateWidgetCommand, Result>(
                new CreateWidgetCommand(), Result.Failure("nope"), NewUser());

            notifier.Verify(
                n => n.NotifyEntityChangedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // A query (not in a .Commands namespace) is a read — it must never broadcast.
        [Fact]
        public async Task Query_Does_Not_Broadcast()
        {
            var (_, notifier) = await RunAsync<GetWidgetsQuery, Result>(
                new GetWidgetsQuery(), Result.Success(), NewUser());

            notifier.Verify(
                n => n.NotifyEntityChangedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // An excluded area (auth/AI/backup) is not clinic list data — no spurious refetch signal.
        [Fact]
        public async Task Excluded_Area_Command_Does_Not_Broadcast()
        {
            var (_, notifier) = await RunAsync<SignInWidgetCommand, Result>(
                new SignInWidgetCommand(), Result.Success(), NewUser());

            notifier.Verify(
                n => n.NotifyEntityChangedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // No authenticated/resolvable user (e.g. first-run setup) → nothing to scope to → no broadcast.
        [Fact]
        public async Task No_Resolved_Clinic_Does_Not_Broadcast()
        {
            var (_, notifier) = await RunAsync<CreateWidgetCommand, Result>(
                new CreateWidgetCommand(), Result.Success(), resolvedUser: null);

            notifier.Verify(
                n => n.NotifyEntityChangedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // [AC-5] A broadcast failure is swallowed — the committed command's response is returned intact.
        [Fact]
        public async Task Broadcast_Failure_Is_Swallowed()
        {
            var notifier = new Mock<IRealtimeNotifier>();
            notifier
                .Setup(n => n.NotifyEntityChangedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("hub unreachable"));

            var response = Result.Success();
            var exception = await Record.ExceptionAsync(() =>
                RunAsync<CreateWidgetCommand, Result>(new CreateWidgetCommand(), response, NewUser(), notifier));

            Assert.Null(exception);
        }
    }
}
