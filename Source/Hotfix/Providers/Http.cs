using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.ServerSentEvents;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hotfix;

internal static class Http
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static Task<HttpResult> GetAsync(string url, IReadOnlyDictionary<string, string> headers,
        CancellationToken ct) =>
        SendAsync(HttpMethod.Get, url, null, headers, null, ct);

    public static Task<HttpResult> PostAsync(string url, IReadOnlyDictionary<string, string> headers, string body,
        CancellationToken ct) =>
        SendAsync(HttpMethod.Post, url, Json(body), headers, null, ct);

    public static Task<HttpResult> PostSseAsync(string url, IReadOnlyDictionary<string, string> headers,
        string body, Action<SseItem<string>> onEvent, TimeSpan idleTimeout, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, url, Json(body), headers,
            (content, token) => ReadSseAsync(content, onEvent, idleTimeout, token), ct);

    private static async Task<HttpResult> SendAsync(HttpMethod method, string url, HttpContent? content,
        IReadOnlyDictionary<string, string> headers, Func<HttpContent, CancellationToken, Task<string>>? readBody,
        CancellationToken ct)
    {
        var status = 0;
        try
        {
            using var request = NewRequest(method, url, content, headers);
            var option = readBody is null
                ? HttpCompletionOption.ResponseContentRead
                : HttpCompletionOption.ResponseHeadersRead;
            using var response = await Client.SendAsync(request, option, ct).ConfigureAwait(false);
            status = (int)response.StatusCode;
            var body = readBody is not null && response.IsSuccessStatusCode
                ? await readBody(response.Content, ct).ConfigureAwait(false)
                : await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new HttpResult(status, body, RetryAfterOf(response));
        }
        catch (Exception e)
        {
            return new HttpResult(status, "", Exception: e);
        }
    }

    private static async Task<string> ReadSseAsync(HttpContent content, Action<SseItem<string>> onEvent,
        TimeSpan idleTimeout, CancellationToken ct)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(idleTimeout);
        try
        {
            await using var stream = await content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
            await foreach (var item in SseParser.Create(stream).EnumerateAsync(idle.Token).ConfigureAwait(false))
            {
                idle.CancelAfter(idleTimeout);
                onEvent(item);
            }
        }
        catch (OperationCanceledException) when (idle.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TaskCanceledException("SSE idle timeout", new TimeoutException());
        }
        return "";
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static HttpRequestMessage NewRequest(HttpMethod method, string url, HttpContent? content,
        IReadOnlyDictionary<string, string> headers)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        foreach (var (key, value) in headers)
            request.Headers.TryAddWithoutValidation(key, value);
        return request;
    }

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta;
        if (retryAfter?.Date is { } date)
            return date - DateTimeOffset.UtcNow;
        return null;
    }
}