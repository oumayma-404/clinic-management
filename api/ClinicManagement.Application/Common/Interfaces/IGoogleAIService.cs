using ClinicManagement.Application.Features.AI.Commands;

namespace ClinicManagement.Application.Common.Interfaces;

public interface IGoogleAIService
{
    Task<GoogleAIResponse> ChatAsync(
        List<GoogleAIMessage> messages,
        Application.Features.AI.Commands.ChatContextDto? context = null,
        CancellationToken cancellationToken = default);
}

public class GoogleAIMessage
{
    public string Role { get; set; } = string.Empty; // "user" or "model"
    public string Content { get; set; } = string.Empty;
}

public class GoogleAIResponse
{
    public string Message { get; set; } = string.Empty;
    public GoogleAITokenUsage? Usage { get; set; }
}

public class GoogleAITokenUsage
{
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
}

