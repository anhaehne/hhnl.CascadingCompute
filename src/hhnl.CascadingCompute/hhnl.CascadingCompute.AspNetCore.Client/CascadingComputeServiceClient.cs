using hhnl.CascadingCompute.AspNetCore.Shared.Models;
using hhnl.CascadingCompute.Caching;
using System.Collections.Concurrent;
using System.Net.ServerSentEvents;
using System.Text.Json;

namespace hhnl.CascadingCompute.AspNetCore.Client;

public abstract class CascadingComputeServiceClient<TService>(HttpClient httpClient) : IDisposable
    where TService : class
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<IDependentCacheEntry>> _cacheEntries = new();

    CancellationTokenSource? _invalidationCts;

    protected readonly HttpClient _httpClient = httpClient;

    protected abstract string BaseRoute { get; }

    public async void Start()
    {
        _invalidationCts = new();

        var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, $"{BaseRoute}/invalidations");
        //httpRequestMessage.SetBrowserResponseStreamingEnabled(true);
        httpRequestMessage.Headers.Add("Accept", "text/event-stream");

        OnRequestMessageCreated(httpRequestMessage);

        var response = await _httpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead, _invalidationCts.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        await foreach (var hb in SseParser.Create(stream).EnumerateAsync().WithCancellation(_invalidationCts.Token))
        {
            var invalidation = JsonSerializer.Deserialize<InvalidationDto>(hb.Data, JsonSerializerOptions.Web);

            if (invalidation is null)
                continue;

            if (_cacheEntries.TryRemove(invalidation.Url, out var entries))
            {
                foreach (var e in entries)
                    e.Invalidate();
            }
        }
    }

    public void Dispose()
    {
        _invalidationCts?.Cancel();
        _invalidationCts?.Dispose();
    }

    protected virtual void OnRequestMessageCreated(HttpRequestMessage requestMessage) { }

    protected void CaptureCacheContext(string url)
    {
        if (CacheDependencyContext.CurrentEntry.Value is null)
            return;

        var entries = _cacheEntries.GetOrAdd("/" + url, static _ => []);
        entries.Add(CacheDependencyContext.CurrentEntry.Value);
    }
}
