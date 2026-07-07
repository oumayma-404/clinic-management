using ClinicManagement.Application.Features.AI.Commands;

namespace ClinicManagement.Application.Common.Interfaces;

public interface IAIActionService
{
    Task<AIActionResult> ExecuteActionAsync(AIActionRequest request, CancellationToken cancellationToken = default);
}

public class AIActionRequest
{
    public string UserMessage { get; set; } = string.Empty;
    public List<ChatMessageDto> ConversationHistory { get; set; } = new();
    public ChatContextDto? Context { get; set; }
}

public class AIActionResult
{
    public bool ShouldExecuteAction { get; set; }
    public string? ActionType { get; set; } // "create_appointment", "search_patient", etc.
    public Dictionary<string, object>? ActionParameters { get; set; }
    public string? ResponseMessage { get; set; }
    public object? ActionResult { get; set; }
}



