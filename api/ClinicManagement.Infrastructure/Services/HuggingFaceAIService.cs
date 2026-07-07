using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.AI.Commands;
using ChatContextDto = ClinicManagement.Application.Features.AI.Commands.ChatContextDto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace ClinicManagement.Infrastructure.Services;

public class HuggingFaceAIService : IHuggingFaceAIService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<HuggingFaceAIService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string? _model;

    public HuggingFaceAIService(
        IConfiguration configuration,
        ILogger<HuggingFaceAIService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _apiKey = _configuration["HuggingFace:ApiKey"];
        // Default model - using a valid public model that works with free inference API
        _model = _configuration["HuggingFace:Model"] ?? "microsoft/Phi-3-mini-4k-instruct";

        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("Hugging Face API key is not configured");
        }

        // Set authorization header
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        }
    }

    public async Task<HuggingFaceAIResponse> ChatAsync(
        List<HuggingFaceAIMessage> messages,
        ChatContextDto? context = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException("Hugging Face API key is not configured");
        }

        if (string.IsNullOrEmpty(_model))
        {
            throw new InvalidOperationException("Hugging Face model is not configured");
        }

        try
        {
            // Build system prompt with clinic context
            var systemPrompt = BuildSystemPrompt(context);
            
            // Router endpoint uses OpenAI-compatible chat completions format
            // Convert messages to the chat format
            var chatMessages = new List<Dictionary<string, string>>();

            // Add system message if available
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                chatMessages.Add(new Dictionary<string, string>
                {
                    ["role"] = "system",
                    ["content"] = systemPrompt
                });
            }

            // Add conversation messages
            foreach (var message in messages)
            {
                // Convert "model" role to "assistant" for OpenAI format
                var role = message.Role == "model" ? "assistant" : message.Role;
                chatMessages.Add(new Dictionary<string, string>
                {
                    ["role"] = role,
                    ["content"] = message.Content
                });
            }

            // Build request body for OpenAI-compatible chat completions format
            // Format matches: https://router.huggingface.co/v1/chat/completions
            var requestBody = new Dictionary<string, object>
            {
                ["model"] = _model,
                ["messages"] = chatMessages,
                ["stream"] = false
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Router endpoint uses OpenAI-compatible chat completions format
            // Format: https://router.huggingface.co/v1/chat/completions
            var url = "https://router.huggingface.co/v1/chat/completions";

            _logger.LogInformation("Calling Hugging Face API with model {Model} at {Endpoint}", _model, url);

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Hugging Face API error: {StatusCode} - {Response}", response.StatusCode, responseJson);
                
                // Check if model is loading
                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || 
                    responseJson.Contains("loading") || responseJson.Contains("is currently loading"))
                {
                    // Wait a bit and retry once
                    _logger.LogInformation("Model is loading, waiting 5 seconds and retrying...");
                    await Task.Delay(5000, cancellationToken);
                    response = await _httpClient.PostAsync(url, content, cancellationToken);
                    responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"Hugging Face API returned {response.StatusCode}: {responseJson}");
                    }
                }
                else
                {
                    throw new HttpRequestException($"Hugging Face API returned {response.StatusCode}: {responseJson}");
                }
            }

            var responseData = JsonDocument.Parse(responseJson).RootElement;

            // Check for error in response
            if (responseData.TryGetProperty("error", out var error))
            {
                var errorMessage = error.GetString() ?? "Unknown error from Hugging Face API";
                _logger.LogError("Hugging Face API error: {Error}", errorMessage);
                throw new InvalidOperationException($"Hugging Face API error: {errorMessage}");
            }

            // Extract message from OpenAI-compatible chat completions response
            // Format: {"choices": [{"message": {"role": "assistant", "content": "..."}}]}
            string responseMessage;
            
            if (responseData.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message))
                {
                    if (message.TryGetProperty("content", out var messageContent))
                    {
                        responseMessage = messageContent.GetString() ?? "I apologize, but I couldn't generate a response.";
                    }
                    else
                    {
                        _logger.LogWarning("Message object found but no content property: {Message}", message.GetRawText());
                        responseMessage = "I apologize, but I couldn't generate a response.";
                    }
                }
                else
                {
                    _logger.LogWarning("Choices found but no message property: {Choice}", firstChoice.GetRawText());
                    responseMessage = "I apologize, but I couldn't generate a response.";
                }
            }
            else
            {
                // Fallback: try to extract any text content
                _logger.LogWarning("Unexpected Hugging Face response format: {Response}", responseJson);
                responseMessage = "I apologize, but I couldn't generate a response.";
            }

            var usage = new HuggingFaceAITokenUsage();
            // Hugging Face doesn't always provide token usage, but some models do
            if (responseData.TryGetProperty("usage", out var usageProp))
            {
                usage.PromptTokens = usageProp.TryGetProperty("prompt_tokens", out var promptTokens)
                    ? promptTokens.GetInt32()
                    : null;
                usage.CompletionTokens = usageProp.TryGetProperty("completion_tokens", out var completionTokens)
                    ? completionTokens.GetInt32()
                    : null;
                usage.TotalTokens = usageProp.TryGetProperty("total_tokens", out var totalTokens)
                    ? totalTokens.GetInt32()
                    : null;
            }

            _logger.LogInformation("Successfully received response from Hugging Face model {Model}", _model);
            return new HuggingFaceAIResponse
            {
                Message = responseMessage,
                Usage = usage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Hugging Face API");
            throw;
        }
    }

    private string BuildSystemPrompt(ChatContextDto? context)
    {
        var prompt = "You are a helpful AI assistant for a clinic management system. " +
                    "You help doctors, nurses, and administrative staff with various tasks related to patient care, " +
                    "appointments, medical records, and clinic operations. " +
                    "Be professional, concise, and helpful. " +
                    "Always prioritize patient privacy and confidentiality.";

        if (context?.PatientId != null)
        {
            prompt += $"\n\nCurrent context: The user is working with patient ID {context.PatientId}.";
        }

        if (context?.AppointmentId != null)
        {
            prompt += $"\nCurrent context: The user is working with appointment ID {context.AppointmentId}.";
        }

        if (context?.DoctorId != null)
        {
            prompt += $"\nCurrent context: The logged-in doctor ID is {context.DoctorId}. When creating appointments or performing actions, use this doctor unless the user specifies otherwise.";
        }

        return prompt;
    }
}

