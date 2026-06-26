using ClinicManagement.Application.Features.AI.Commands;

namespace ClinicManagement.Application.Common.Interfaces;

public interface IHuggingFaceAIService
{
    Task<HuggingFaceAIResponse> ChatAsync(
        List<HuggingFaceAIMessage> messages,
        Application.Features.AI.Commands.ChatContextDto? context = null,
        CancellationToken cancellationToken = default);
}

public class HuggingFaceAIMessage
{
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
}

public class HuggingFaceAIResponse
{
    public string Message { get; set; } = string.Empty;
    public HuggingFaceAITokenUsage? Usage { get; set; }
}

public class HuggingFaceAITokenUsage
{
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
}



