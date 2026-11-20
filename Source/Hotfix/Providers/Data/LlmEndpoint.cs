namespace Hotfix.Data;

public sealed class LlmEndpoint(string apiKey, string? baseUrl)
{
    public string ApiKey { get; } = apiKey;
    public string? BaseUrl { get; } = baseUrl;

    public override string ToString()
    {
        var apiKeyMasked = string.IsNullOrWhiteSpace(ApiKey) ? "" : "****";
        return $"LlmEndpoint {{ ApiKey: {apiKeyMasked}, BaseUrl: {BaseUrl} }}";
    }
}
