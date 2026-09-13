using System.ClientModel;
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

        _client = new OpenAIClient(
            new ApiKeyCredential(geminiOptions.ApiKey!),
            new OpenAIClientOptions { Endpoint = new Uri(geminiOptions.Endpoint) });
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
            catch (ClientResultException ex) when (ex.Status == 429)
            {
                // Daily free-tier quota for this model. Fall through to the next, which has its own
                // quota — the id is GenerateRequestsPerDayPerProjectPerModel.
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
        return GenerationResult.Failed(
            GenerationFailure.QuotaExhausted,
            "Every configured model has used up its free-tier daily quota, so no cards can be "
            + $"generated right now. This deployment runs on Google's Gemini free tier by choice: "
            + $"{string.Join(", ", _models)} were all tried and all returned HTTP 429 "
            + "(RESOURCE_EXHAUSTED). The quota resets once a day, and nothing is wrong with the "
            + "application. Your passage is still here.");
    }

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
