using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Common.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        // Kept: a « Handling » with no « Handled » after it is what names the request in flight at a hang or a crash.
        _logger.LogInformation("Handling {RequestName}", requestName);

        var started = Stopwatch.GetTimestamp();
        var response = await next();

        _logger.LogInformation(
            "Handled {RequestName} in {ElapsedMs:F0} ms", requestName, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return response;
    }
}
