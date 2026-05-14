using Blazored.LocalStorage;

namespace AvdpSmartFleet.DriverApp.Services;

/// <summary>
/// Persists trip log requests in browser localStorage when the network is unavailable,
/// then replays them when connectivity returns. Each entry has a local UUID for server reconciliation.
/// </summary>
public class OfflineQueue
{
    private const string Key = "trip_log_queue";
    private readonly ILocalStorageService _storage;
    private readonly ApiClient _api;

    public OfflineQueue(ILocalStorageService storage, ApiClient api)
    {
        _storage = storage; _api = api;
    }

    public async Task EnqueueOrSendAsync(TripLogRequest req)
    {
        var withUuid = req with { LocalUuid = req.LocalUuid ?? Guid.NewGuid().ToString() };
        var ok = await TrySendAsync(withUuid);
        if (!ok) await EnqueueAsync(withUuid);
    }

    public async Task<int> FlushAsync()
    {
        var queue = await _storage.GetItemAsync<List<TripLogRequest>>(Key) ?? new();
        int sent = 0;
        var remaining = new List<TripLogRequest>();
        foreach (var q in queue)
        {
            if (await TrySendAsync(q)) sent++;
            else remaining.Add(q);
        }
        await _storage.SetItemAsync(Key, remaining);
        return sent;
    }

    public async Task<int> PendingCountAsync()
    {
        var queue = await _storage.GetItemAsync<List<TripLogRequest>>(Key);
        return queue?.Count ?? 0;
    }

    private async Task<bool> TrySendAsync(TripLogRequest req)
    {
        try { return await _api.LogEventAsync(req); }
        catch { return false; }
    }

    private async Task EnqueueAsync(TripLogRequest req)
    {
        var queue = await _storage.GetItemAsync<List<TripLogRequest>>(Key) ?? new();
        queue.Add(req);
        await _storage.SetItemAsync(Key, queue);
    }
}
