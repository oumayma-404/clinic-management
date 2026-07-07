using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using HuggingFaceAIMessage = ClinicManagement.Application.Common.Interfaces.HuggingFaceAIMessage;

namespace ClinicManagement.Application.Features.AI.Commands;

public class ChatCommandHandler : IRequestHandler<ChatCommand, Result<ChatResponse>>
{
    private readonly IHuggingFaceAIService _huggingFaceAIService;
    private readonly IAIActionService _actionService;
    private readonly ILogger<ChatCommandHandler> _logger;

    public ChatCommandHandler(
        IHuggingFaceAIService huggingFaceAIService,
        IAIActionService actionService,
        ILogger<ChatCommandHandler> logger)
    {
        _huggingFaceAIService = huggingFaceAIService;
        _actionService = actionService;
        _logger = logger;
    }

    public async Task<Result<ChatResponse>> Handle(ChatCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Get the last user message
            var lastUserMessage = request.Messages.LastOrDefault(m => m.Role == "user");
            if (lastUserMessage == null)
            {
                return Result<ChatResponse>.Failure("No user message found");
            }

            // Check if user wants to perform an action
            var actionRequest = new AIActionRequest
            {
                UserMessage = lastUserMessage.Content,
                ConversationHistory = request.Messages,
                Context = request.Context
            };

            var actionResult = await _actionService.ExecuteActionAsync(actionRequest, cancellationToken);

            // If an action was executed, return the action result message
            if (actionResult.ShouldExecuteAction && !string.IsNullOrEmpty(actionResult.ResponseMessage))
            {
                return Result<ChatResponse>.Success(new ChatResponse
                {
                    Message = actionResult.ResponseMessage,
                    Usage = null
                });
            }

            // Otherwise, proceed with normal AI chat
            // Convert DTOs to service format
            // Hugging Face uses "assistant" for AI responses
            var messages = request.Messages.Select(m => new HuggingFaceAIMessage
            {
                Role = m.Role == "assistant" ? "assistant" : m.Role,
                Content = m.Content
            }).ToList();

            // Call Hugging Face AI service
            var response = await _huggingFaceAIService.ChatAsync(messages, request.Context, cancellationToken);

            return Result<ChatResponse>.Success(new ChatResponse
            {
                Message = response.Message,
                Usage = response.Usage != null ? new TokenUsageDto
                {
                    PromptTokens = response.Usage.PromptTokens,
                    CompletionTokens = response.Usage.CompletionTokens,
                    TotalTokens = response.Usage.TotalTokens
                } : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing AI chat request");
            return Result<ChatResponse>.Failure($"Error processing chat: {ex.Message}");
        }
    }
}

