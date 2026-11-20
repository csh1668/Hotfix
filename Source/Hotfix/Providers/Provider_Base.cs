using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hotfix.Data;

namespace Hotfix;

public abstract class Provider_Base
{
    public abstract string Id { get; }
    public abstract string Label { get; }
    public abstract string DefaultBaseUrl { get; }

    public abstract Task<LlmResponse> CallAsync(LlmRequest request, IProgress<LlmStreamDelta>? progress = null,
        CancellationToken ct = default);
    public abstract Task<(IReadOnlyList<string> Models, LlmError? Error)> GetModelsAsync(LlmEndpoint endpoint,
        CancellationToken ct = default);

    protected string BaseUrlOf(LlmEndpoint endpoint) =>
        string.IsNullOrEmpty(endpoint.BaseUrl) ? DefaultBaseUrl : endpoint.BaseUrl;
}
