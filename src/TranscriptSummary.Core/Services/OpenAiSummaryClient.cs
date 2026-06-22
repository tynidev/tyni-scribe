using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TranscriptSummary.Core.Services;

public sealed class OpenAiSummaryClient(HttpClient httpClient) : IOpenAiSummaryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<OpenAiSummaryResult> SummarizeAsync(OpenAiSummaryRequest request, CancellationToken cancellationToken = default)
    {
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

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

        using var response = await httpClient.PostAsJsonAsync(request.Endpoint, payload, JsonOptions, timeout.Token);
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

        return new OpenAiSummaryResult(content.Trim());
    }

    private sealed record ChatCompletionRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        bool Stream);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatCompletionResponse(IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(ChatMessage? Message);
}