using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StarlightAI.Engine;

public sealed class StarlightAIConfig
{
    public string Model { get; set; } = "gemini-2.5-flash";
    public string? SystemInstruction { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public long? TopK { get; set; }
    public long? MaxOutputTokens { get; set; }
    public List<string> StopSequences { get; set; } = new();
    public string? ResponseMimeType { get; set; }
    public object? ResponseSchema { get; set; }
    public long? ThinkingBudget { get; set; }
    public string? SafetyThreshold { get; set; }

    public StarlightAIConfig Clone()
    {
        return new StarlightAIConfig
        {
            Model = Model,
            SystemInstruction = SystemInstruction,
            Temperature = Temperature,
            TopP = TopP,
            TopK = TopK,
            MaxOutputTokens = MaxOutputTokens,
            StopSequences = new List<string>(StopSequences),
            ResponseMimeType = ResponseMimeType,
            ResponseSchema = ResponseSchema,
            ThinkingBudget = ThinkingBudget,
            SafetyThreshold = SafetyThreshold
        };
    }
}

public sealed record ChatTurn(string Role, string Text);

public sealed class StarlightAIChat
{
    public StarlightAIConfig Config { get; }
    public List<ChatTurn> History { get; } = new();

    public StarlightAIChat(StarlightAIConfig? config = null)
    {
        Config = config?.Clone() ?? new StarlightAIConfig();
    }
}

public sealed class StarlightAIResponse
{
    public string Text { get; set; } = string.Empty;
    public string FinishReason { get; set; } = "UNKNOWN";
    public long PromptTokens { get; set; }
    public long CandidateTokens { get; set; }
    public long TotalTokens { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
}

public sealed class GeminiService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    private static readonly Regex SafeModelRegex = new(@"^[a-zA-Z0-9._\-]+$", RegexOptions.Compiled);

    private static readonly string[] HarmCategories =
    {
        "HARM_CATEGORY_HARASSMENT",
        "HARM_CATEGORY_HATE_SPEECH",
        "HARM_CATEGORY_SEXUALLY_EXPLICIT",
        "HARM_CATEGORY_DANGEROUS_CONTENT"
    };

    private static string GetApiKeyOrThrow()
    {
        string? key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            throw new StarlightException("AI_KEY_UNSET");
        return key.Trim();
    }

    private static string NormalizeModel(string? model)
    {
        string m = string.IsNullOrWhiteSpace(model) ? "gemini-2.5-flash" : model.Trim();
        if (m.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            m = m[7..];
        if (!SafeModelRegex.IsMatch(m))
            m = "gemini-2.5-flash";
        return m;
    }

    public async Task<StarlightAIResponse> GenerateAsync(
        List<ChatTurn> turns,
        StarlightAIConfig config,
        CancellationToken ct = default)
    {
        string apiKey = GetApiKeyOrThrow();
        string model = NormalizeModel(config.Model);
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

        var payload = new Dictionary<string, object>();

        // Contents
        var contents = new List<object>();
        foreach (var turn in turns)
        {
            string role = turn.Role == "model" ? "model" : "user";
            contents.Add(new Dictionary<string, object>
            {
                ["role"] = role,
                ["parts"] = new object[]
                {
                    new Dictionary<string, object> { ["text"] = turn.Text ?? string.Empty }
                }
            });
        }
        payload["contents"] = contents;

        // System Instruction
        if (!string.IsNullOrWhiteSpace(config.SystemInstruction))
        {
            payload["system_instruction"] = new Dictionary<string, object>
            {
                ["parts"] = new object[]
                {
                    new Dictionary<string, object> { ["text"] = config.SystemInstruction! }
                }
            };
        }

        // Generation Config
        var genCfg = new Dictionary<string, object>();
        if (config.Temperature.HasValue)
            genCfg["temperature"] = Math.Clamp(config.Temperature.Value, 0.0, 2.0);
        if (config.TopP.HasValue)
            genCfg["topP"] = Math.Clamp(config.TopP.Value, 0.0, 1.0);
        if (config.TopK.HasValue && config.TopK.Value > 0)
            genCfg["topK"] = config.TopK.Value;
        if (config.MaxOutputTokens.HasValue && config.MaxOutputTokens.Value > 0)
            genCfg["maxOutputTokens"] = config.MaxOutputTokens.Value;
        if (config.StopSequences is { Count: > 0 })
            genCfg["stopSequences"] = config.StopSequences;
        if (!string.IsNullOrWhiteSpace(config.ResponseMimeType))
            genCfg["responseMimeType"] = config.ResponseMimeType!;
        if (config.ResponseSchema != null)
            genCfg["responseSchema"] = config.ResponseSchema;
        if (config.ThinkingBudget.HasValue && config.ThinkingBudget.Value >= 0)
        {
            genCfg["thinkingConfig"] = new Dictionary<string, object>
            {
                ["thinkingBudget"] = config.ThinkingBudget.Value
            };
        }
        if (genCfg.Count > 0)
            payload["generationConfig"] = genCfg;

        // Safety Settings
        if (!string.IsNullOrWhiteSpace(config.SafetyThreshold))
        {
            string threshold = config.SafetyThreshold!.Trim().ToUpperInvariant();
            if (threshold is "BLOCK_NONE" or "BLOCK_ONLY_HIGH" or "BLOCK_MEDIUM_AND_ABOVE" or "BLOCK_LOW_AND_ABOVE")
            {
                var safetyList = new List<object>();
                foreach (string cat in HarmCategories)
                {
                    safetyList.Add(new Dictionary<string, string>
                    {
                        ["category"] = cat,
                        ["threshold"] = threshold
                    });
                }
                payload["safetySettings"] = safetyList;
            }
        }

        string jsonBody = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("x-goog-api-key", apiKey);
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        try
        {
            using var resp = await Http.SendAsync(req, ct);
            string respJson = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                return new StarlightAIResponse
                {
                    Text = string.Empty,
                    FinishReason = "API_ERROR",
                    ModelVersion = model,
                    IsSuccess = false
                };
            }

            using var doc = JsonDocument.Parse(respJson);
            var root = doc.RootElement;

            var sbText = new StringBuilder();
            string finishReason = "STOP";

            if (root.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array &&
                candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                if (first.TryGetProperty("finishReason", out var frProp) && frProp.ValueKind == JsonValueKind.String)
                {
                    finishReason = frProp.GetString() ?? "STOP";
                }
                if (first.TryGetProperty("content", out var contentObj) &&
                    contentObj.TryGetProperty("parts", out var partsArr) &&
                    partsArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in partsArr.EnumerateArray())
                    {
                        if (part.TryGetProperty("thought", out var thoughtProp) &&
                            thoughtProp.ValueKind == JsonValueKind.True)
                        {
                            continue;
                        }
                        if (part.TryGetProperty("text", out var textProp) &&
                            textProp.ValueKind == JsonValueKind.String)
                        {
                            sbText.Append(textProp.GetString());
                        }
                    }
                }
            }

            long promptTok = 0, candTok = 0, totalTok = 0;
            if (root.TryGetProperty("usageMetadata", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("promptTokenCount", out var p) && p.TryGetInt64(out long pv))
                    promptTok = pv;
                if (usage.TryGetProperty("candidatesTokenCount", out var c) && c.TryGetInt64(out long cv))
                    candTok = cv;
                if (usage.TryGetProperty("totalTokenCount", out var t) && t.TryGetInt64(out long tv))
                    totalTok = tv;
            }

            string modelVer = model;
            if (root.TryGetProperty("modelVersion", out var mvProp) && mvProp.ValueKind == JsonValueKind.String)
                modelVer = mvProp.GetString() ?? model;

            return new StarlightAIResponse
            {
                Text = sbText.ToString(),
                FinishReason = finishReason,
                PromptTokens = promptTok,
                CandidateTokens = candTok,
                TotalTokens = totalTok,
                ModelVersion = modelVer,
                IsSuccess = true
            };
        }
        catch (StarlightException)
        {
            throw;
        }
        catch
        {
            throw new StarlightException("AI_CALL_FAILED");
        }
    }

    public async Task<long> CountTokensAsync(string text, string? model = null, CancellationToken ct = default)
    {
        string apiKey = GetApiKeyOrThrow();
        string normModel = NormalizeModel(model);
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{normModel}:countTokens";

        var payload = new Dictionary<string, object>
        {
            ["contents"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["parts"] = new object[]
                    {
                        new Dictionary<string, object> { ["text"] = text ?? string.Empty }
                    }
                }
            }
        };

        string jsonBody = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("x-goog-api-key", apiKey);
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        try
        {
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return 0;

            string respJson = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(respJson);
            if (doc.RootElement.TryGetProperty("totalTokens", out var tt) && tt.TryGetInt64(out long count))
                return count;
            return 0;
        }
        catch
        {
            throw new StarlightException("AI_COUNT_FAILED");
        }
    }
}
