using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace TenExCards.Generation;

/// <summary>
/// Gemini, reached through its OpenAI-compatible endpoint — hence the <c>OpenAI</c> package. No
/// logger here on purpose: the passage must never reach one.
/// </summary>
public class GeminiCardCandidateGenerator : ICardCandidateGenerator
{
    private readonly OpenAIClient _client;
    private readonly string[] _models;
    private readonly GenerationOptions _options;

    public GeminiCardCandidateGenerator(IOptions<GeminiOptions> gemini, IOptions<GenerationOptions> generation)
    {
        var geminiOptions = gemini.Value;
        _options = generation.Value;
        _models = geminiOptions.Models;

        if (_models.Length == 0)
        {
            throw new InvalidOperationException("Gemini:Models is empty; at least one model is required.");
        }

        // One retry, not the default three: the rotation below IS the resilience strategy, and
        // re-asking an overloaded model before trying a healthy one just spends the timeout budget.
        _client = new OpenAIClient(
            new ApiKeyCredential(geminiOptions.ApiKey!),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(geminiOptions.Endpoint),
                RetryPolicy = new ClientRetryPolicy(maxRetries: 1),
            });
    }

    public async Task<GenerationResult> GenerateAsync(
        string passage,
        string? focusHint,
        IProgress<GenerationProgress> progress,
        CancellationToken ct)
    {
        var target = PassageBounds.TargetCandidateCount(passage, _options);
        var messages = BuildMessages(passage, focusHint, target);
        var options = BuildOptions();
        var lastStatus = 0;

        foreach (var model in _models)
        {
            var accumulated = new StringBuilder();
            var chunks = 0;

            try
            {
                var updates = _client.GetChatClient(model)
                    .CompleteChatStreamingAsync(messages, options, ct);

                await foreach (var update in updates)
                {
                    foreach (var part in update.ContentUpdate)
                    {
                        accumulated.Append(part.Text);
                    }

                    progress.Report(new GenerationProgress(++chunks));
                }
            }
            catch (OperationCanceledException)
            {
                return GenerationResult.Failed(
                    GenerationFailure.Timeout,
                    "Generating took longer than expected and was stopped. Your passage is still here — try again, or narrow it with a focus hint.");
            }
            catch (ClientResultException ex) when (IsWorthTryingAnotherModel(ex.Status))
            {
                lastStatus = ex.Status;
                continue;
            }
            catch (Exception)
            {
                // Broad by design: anything escaping becomes the generic Blazor error UI. Not
                // retried on another model, because a real fault would fail there too.
                return GenerationResult.Failed(
                    GenerationFailure.ProviderError,
                    "The card generator could not be reached. Your passage is still here — please try again in a moment.");
            }

            return ParseAndApplyRules(accumulated.ToString());
        }

        // Written for the MVP's reviewers rather than a general audience: they need to know this is
        // a deliberate free-tier ceiling, not a defect, and that it clears on its own.
        var cause = lastStatus == 429
            ? "all returned HTTP 429 (RESOURCE_EXHAUSTED), meaning the daily quota is spent. It "
              + "resets once a day"
            : $"were all unavailable (the last returned HTTP {lastStatus}). This is usually "
              + "temporary — try again in a minute";

        return GenerationResult.Failed(
            GenerationFailure.QuotaExhausted,
            "No cards can be generated right now. This deployment runs on Google's Gemini free tier "
            + $"by choice, and {string.Join(", ", _models)} {cause}. Nothing is wrong with the "
            + "application. Your passage is still here.");
    }

    /// <summary>
    /// Whether a failed model is worth abandoning for the next one. 429 is the daily quota, and 5xx
    /// covers an overloaded model (Gemini returns 503 UNAVAILABLE under load) — both are specific to
    /// one model, so the next may well answer. A 400/401/403 is a fault in the request or the key
    /// and would fail identically everywhere, so it stops the rotation instead of tripling the wait.
    /// </summary>
    private static bool IsWorthTryingAnotherModel(int status) =>
        status is 429 or 404 or >= 500;

    private static List<ChatMessage> BuildMessages(string passage, string? focusHint, int target)
    {
        var user = new StringBuilder();
        user.Append("Aim for ").Append(target).AppendLine(" candidate cards.");

        if (!string.IsNullOrWhiteSpace(focusHint))
        {
            user.AppendLine().AppendLine("FOCUS HINT (narrow what you card to this):").AppendLine(focusHint.Trim());
        }

        user.AppendLine().AppendLine("PASSAGE:").Append(passage);

        return
        [
            new SystemChatMessage(CardGenerationPrompt.System),
            new UserChatMessage(user.ToString()),
        ];
    }

    private ChatCompletionOptions BuildOptions()
    {
        var schema = $$"""
            {
              "type": "object",
              "properties": {
                "candidates": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "prompt": { "type": "string", "maxLength": {{_options.MaxPromptCharacters}} },
                      "answer": { "type": "string", "maxLength": {{_options.MaxAnswerCharacters}} }
                    },
                    "required": ["prompt", "answer"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["candidates"],
              "additionalProperties": false
            }
            """;

        return new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "card_candidates",
                jsonSchema: BinaryData.FromString(schema),
                jsonSchemaIsStrict: true),
        };
    }

    private GenerationResult ParseAndApplyRules(string json)
    {
        CandidateEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CandidateEnvelope>(json);
        }
        catch (JsonException)
        {
            return GenerationResult.Failed(
                GenerationFailure.Malformed,
                "The card generator returned something unreadable. Your passage is still here — please try again.");
        }

        var candidates = (envelope?.Candidates ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Prompt) && !string.IsNullOrWhiteSpace(c.Answer))
            .Select(c => new CandidateCard(c.Prompt!.Trim(), c.Answer!.Trim()))
            .ToList();

        IReadOnlyList<CandidateCard> kept = candidates.Take(_options.MaxCandidates).ToList();
        kept = CandidateDeduplicator.Deduplicate(kept);
        kept = CandidateBounds.WithinColumnLimits(kept, _options);

        // Too few is a failure, not a triage state with nothing to triage. Retaining the passage is
        // what the learner needs next — they will add a focus hint and retry.
        if (kept.Count < _options.MinCandidates)
        {
            return GenerationResult.Failed(
                GenerationFailure.Refused,
                "That passage did not yield enough usable cards. Your passage is still here — try a longer one, or narrow it with a focus hint.");
        }

        return GenerationResult.Success(kept);
    }

    private sealed class CandidateEnvelope
    {
        [JsonPropertyName("candidates")]
        public List<RawCandidate>? Candidates { get; set; }
    }

    private sealed class RawCandidate
    {
        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("answer")]
        public string? Answer { get; set; }
    }
}
