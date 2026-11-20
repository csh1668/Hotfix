using System;
using System.Collections.Generic;
using Hotfix.Data;
using Verse;

namespace Hotfix;

public sealed class LlmProfile : IExposable
{
    public string Id = Guid.NewGuid().ToString("N");
    public string Name = "";
    public string ProviderId = Providers.List[0].Id;
    public string ApiKey = "";
    public string? BaseUrl;
    public string Model = "";
    private const int DefaultContextWindow = 200_000;

    public LlmEffort? Effort;
    public int ContextWindow = DefaultContextWindow;

    public float InputPrice;
    public float OutputPrice;
    public float CacheReadPrice;
    public float CacheWritePrice;

    public Provider_Base? Provider => Providers.TryGet(ProviderId, out var provider) ? provider : null;

    public LlmEndpoint Endpoint => new(ApiKey, BaseUrl);

    public double CostOf(LlmTokenUsage usage) => ((double)usage.InputTokens * InputPrice + (double)usage.OutputTokens * OutputPrice
        + (double)usage.CacheReadTokens * CacheReadPrice + (double)usage.CacheWriteTokens * CacheWritePrice) / 1_000_000;

    public LlmRequest CreateRequest(IReadOnlyList<LlmMessage> messages) => new()
    {
        Endpoint = Endpoint,
        Model = Model,
        Effort = Effort,
        Messages = messages,
    };

    public void CopyFrom(LlmProfile other)
    {
        Name = other.Name;
        ProviderId = other.ProviderId;
        ApiKey = other.ApiKey;
        BaseUrl = other.BaseUrl;
        Model = other.Model;
        Effort = other.Effort;
        ContextWindow = other.ContextWindow;
        InputPrice = other.InputPrice;
        OutputPrice = other.OutputPrice;
        CacheReadPrice = other.CacheReadPrice;
        CacheWritePrice = other.CacheWritePrice;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref Id!, "id");
        Scribe_Values.Look(ref Name, "name", "");
        Scribe_Values.Look(ref ProviderId!, "providerId");
        Scribe_Values.Look(ref ApiKey, "apiKey", "");
        Scribe_Values.Look(ref BaseUrl, "baseUrl");
        Scribe_Values.Look(ref Model, "model", "");
        Scribe_Values.Look(ref Effort, "effort");
        Scribe_Values.Look(ref ContextWindow, "contextWindow", DefaultContextWindow);
        Scribe_Values.Look(ref InputPrice, "inputPrice");
        Scribe_Values.Look(ref OutputPrice, "outputPrice");
        Scribe_Values.Look(ref CacheReadPrice, "cacheReadPrice");
        Scribe_Values.Look(ref CacheWritePrice, "cacheWritePrice");
    }
}
