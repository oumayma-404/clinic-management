using MediatR;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Features.AI.Commands;

public class ChatCommand : IRequest<Result<ChatResponse>>
{
    public List<ChatMessageDto> Messages { get; set; } = new();
    public ChatContextDto? Context { get; set; }
}

public class ChatMessageDto
{
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
}

public class ChatContextDto
{
    public Guid? PatientId { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid? DoctorId { get; set; }
}

public class ChatResponse
{
    public string Message { get; set; } = string.Empty;
    public TokenUsageDto? Usage { get; set; }
}

public class TokenUsageDto
{
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
}

