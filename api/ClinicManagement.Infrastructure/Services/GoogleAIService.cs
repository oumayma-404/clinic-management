using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.AI.Commands;
using ChatContextDto = ClinicManagement.Application.Features.AI.Commands.ChatContextDto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace ClinicManagement.Infrastructure.Services;

public class GoogleAIService : IGoogleAIService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleAIService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string? _model;

    public GoogleAIService(
        IConfiguration configuration,
        ILogger<GoogleAIService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _apiKey = _configuration["GoogleAI:ApiKey"];
        // Default model for Google AI Studio - use gemini-2.5-flash (latest) or configured model
        _model = _configuration["GoogleAI:Model"] ?? "gemini-2.5-flash";

        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("Google AI API key is not configured");
        }
    }

    public async Task<GoogleAIResponse> ChatAsync(
        List<GoogleAIMessage> messages,
        ChatContextDto? context = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException("Google AI API key is not configured");
        }

        try
        {
            // Build system prompt with clinic context
            var systemInstruction = BuildSystemPrompt(context);
            
            // Convert messages to Google AI format
            var contents = new List<object>();

            // Add conversation messages
            foreach (var message in messages)
            {
                // GoogleAIMessage already has "model" for assistant messages (converted in handler)
                contents.Add(new
                {
                    role = message.Role, // Already "user" or "model"
                    parts = new[] { new { text = message.Content } }
                });
            }

            var requestBody = new Dictionary<string, object>
            {
                ["contents"] = contents,
                ["generationConfig"] = new Dictionary<string, object>
                {
                    ["temperature"] = 0.7,
                    ["topK"] = 40,
                    ["topP"] = 0.95,
                    ["maxOutputTokens"] = 2048,
                }
            };

            // Add system instruction if available (v1beta supports systemInstruction)
            if (!string.IsNullOrEmpty(systemInstruction))
            {
                requestBody["systemInstruction"] = new Dictionary<string, object>
                {
                    ["parts"] = new[] { new Dictionary<string, string> { ["text"] = systemInstruction } }
                };
            }

            // Google AI Studio API - try different model names (newest first)
            var apiVersion = _configuration["GoogleAI:ApiVersion"] ?? "v1beta";
            var modelsToTry = new[] { _model, "gemini-2.5-flash", "gemini-1.5-flash", "gemini-1.5-pro", "gemini-pro" };
            
            Exception? lastException = null;
            foreach (var modelToTry in modelsToTry)
            {
                try
                {
                    var url = $"https://generativelanguage.googleapis.com/{apiVersion}/models/{modelToTry}:generateContent?key={_apiKey}";
                    var json = JsonSerializer.Serialize(requestBody);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PostAsync(url, content, cancellationToken);
                    var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Google AI API error for model {Model}: {StatusCode} - {Response}", modelToTry, response.StatusCode, responseJson);
                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            lastException = new HttpRequestException($"Google AI API returned {response.StatusCode}: {responseJson}");
                            continue; // Try next model
                        }
                        throw new HttpRequestException($"Google AI API returned {response.StatusCode}: {responseJson}");
                    }

                    var responseData = JsonDocument.Parse(responseJson).RootElement;

                    // Check for error in response
                    if (responseData.TryGetProperty("error", out var error))
                    {
                        var errorMessage = error.TryGetProperty("message", out var msg) 
                            ? msg.GetString() 
                            : "Unknown error from Google AI API";
                        if (errorMessage?.Contains("not found") == true || errorMessage?.Contains("NotFound") == true)
                        {
                            lastException = new InvalidOperationException($"Google AI API error: {errorMessage}");
                            continue; // Try next model
                        }
                        _logger.LogError("Google AI API error: {Error}", errorMessage);
                        throw new InvalidOperationException($"Google AI API error: {errorMessage}");
                    }

                    // Extract message from candidates
                    if (!responseData.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                    {
                        _logger.LogWarning("No candidates in Google AI response");
                        throw new InvalidOperationException("No response candidates from Google AI API");
                    }

                    var firstCandidate = candidates[0];
                    if (!firstCandidate.TryGetProperty("content", out var contentProp) ||
                        !contentProp.TryGetProperty("parts", out var parts) ||
                        parts.GetArrayLength() == 0)
                    {
                        _logger.LogWarning("Invalid candidate structure in Google AI response");
                        throw new InvalidOperationException("Invalid response structure from Google AI API");
                    }

                    var responseMessage = parts[0].TryGetProperty("text", out var textProp)
                        ? textProp.GetString() ?? "I apologize, but I couldn't generate a response."
                        : "I apologize, but I couldn't generate a response.";

                    var usage = new GoogleAITokenUsage();
                    if (responseData.TryGetProperty("usageMetadata", out var usageMetadata))
                    {
                        usage.PromptTokens = usageMetadata.TryGetProperty("promptTokenCount", out var promptTokens) 
                            ? promptTokens.GetInt32() 
                            : null;
                        usage.CompletionTokens = usageMetadata.TryGetProperty("candidatesTokenCount", out var completionTokens) 
                            ? completionTokens.GetInt32() 
                            : null;
                        usage.TotalTokens = usageMetadata.TryGetProperty("totalTokenCount", out var totalTokens) 
                            ? totalTokens.GetInt32() 
                            : null;
                    }

                    _logger.LogInformation("Successfully used model {Model} for AI chat", modelToTry);
                    return new GoogleAIResponse
                    {
                        Message = responseMessage,
                        Usage = usage
                    };
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("404") || ex.Message.Contains("NotFound"))
                {
                    lastException = ex;
                    _logger.LogWarning("Model {Model} not found, trying next model", modelToTry);
                    continue;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found") || ex.Message.Contains("NotFound"))
                {
                    lastException = ex;
                    _logger.LogWarning("Model {Model} not found, trying next model", modelToTry);
                    continue;
                }
            }
            
            // If all models failed, throw the last exception
            throw lastException ?? new InvalidOperationException("No available models found. Please check your API key and model configuration.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Google AI API");
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

