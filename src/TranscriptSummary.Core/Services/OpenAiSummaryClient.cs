using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TranscriptSummary.Core.Services;

public sealed class OpenAiSummaryClient : IOpenAiSummaryClient
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAiSummaryClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<OpenAiSummaryResult> SummarizeAsync(OpenAiSummaryRequest request, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

        var payload = new ChatCompletionRequest(
            request.Model,
            new[]
            {
                new ChatMessage("system", request.SystemPrompt),
                new ChatMessage("user", request.UserText)
            },
            request.MaxOutputTokens,
            Stream: false);

        using var response = await _httpClient.PostAsJsonAsync(request.Endpoint, payload, JsonOptions, timeout.Token);
        var responseText = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI-compatible endpoint returned HTTP {(int)response.StatusCode}.");
        }

        var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responseText, JsonOptions);
        var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("OpenAI-compatible endpoint returned an empty response.");
        }

        return new OpenAiSummaryResult(content.Trim(), completion?.Usage);
    }

    private sealed record ChatCompletionRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        bool Stream);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatCompletionResponse(
        IReadOnlyList<ChatChoice>? Choices,
        OpenAiTokenUsage? Usage);

    private sealed record ChatChoice(ChatMessage? Message);
}