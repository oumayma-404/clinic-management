using ClinicManagement.Application.Common.Behaviors;
using MediatR;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ClinicManagement.UnitTests.Common.Behaviors;

public class LoggingBehaviorTests
{
    public sealed record Ping : IRequest<string>;

    private sealed class CapturingLogger : ILogger<LoggingBehavior<Ping, string>>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }

    [Fact]
    public async Task It_Names_The_Request_Before_It_Runs_And_Times_It_Afterwards()
    {
        var logger = new CapturingLogger();
        var linesWhileRunning = -1;

        var response = await new LoggingBehavior<Ping, string>(logger).Handle(
            new Ping(),
            _ =>
            {
                linesWhileRunning = logger.Lines.Count;
                return Task.FromResult("pong");
            },
            CancellationToken.None);

        Assert.Equal("pong", response);
        Assert.Equal(1, linesWhileRunning);
        Assert.Equal("Handling Ping", logger.Lines[0]);
        Assert.Matches(@"^Handled Ping in \d+ ms$", logger.Lines[1]);
    }
}
