using System.Text.Json;
using System.Web;

namespace AvdpSmartFleet.Api.Services;

/// <summary>
/// Real GPS-Trace / Wialon Hosting HTTP client.
///
/// Auth flow: POST /wialon/ajax.html?svc=token/login&params={"token":"&lt;APP_TOKEN&gt;"} → returns { eid: "&lt;SESSION&gt;" }
/// Messages: POST /wialon/ajax.html?svc=messages/load_interval&params={...}&sid=&lt;SESSION&gt;
///
/// Required appsettings keys:
///   GpsTrace:BaseUrl  e.g. https://hosting.gps-trace.com
///   GpsTrace:Token    application token issued by GPS-Trace
///
/// Session (sid) is re-used until it expires, then refreshed automatically.
/// </summary>
public class GpsTraceHttpClient : IGpsTraceClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<GpsTraceHttpClient> _log;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private string? _sid;
    private DateTime _sidExpiresAt = DateTime.MinValue;

    public GpsTraceHttpClient(HttpClient http, IConfiguration config, ILogger<GpsTraceHttpClient> log)
    {
        _http = http;
        _config = config;
        _log = log;
        var baseUrl = _config["GpsTrace:BaseUrl"] ?? "https://hosting.gps-trace.com";
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    private async Task<string> EnsureSidAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_sid) && DateTime.UtcNow < _sidExpiresAt) return _sid;
        await _sessionLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrEmpty(_sid) && DateTime.UtcNow < _sidExpiresAt) return _sid;
            var token = _config["GpsTrace:Token"]
                        ?? throw new InvalidOperationException("GpsTrace:Token is not configured");
            var paramsJson = JsonSerializer.Serialize(new { token });
            var url = $"wialon/ajax.html?svc=token/login&params={HttpUtility.UrlEncode(paramsJson)}";
            using var resp = await _http.PostAsync(url, null, ct);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.GetInt32() != 0)
                throw new InvalidOperationException($"GPS-Trace login failed: {body}");
            _sid = doc.RootElement.GetProperty("eid").GetString();
            _sidExpiresAt = DateTime.UtcNow.AddMinutes(20);
            _log.LogInformation("GPS-Trace session established");
            return _sid!;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private async Task<JsonDocument> CallAsync(string svc, object @params, CancellationToken ct)
    {
        var sid = await EnsureSidAsync(ct);
        var paramsJson = JsonSerializer.Serialize(@params);
        var url = $"wialon/ajax.html?svc={svc}&params={HttpUtility.UrlEncode(paramsJson)}&sid={sid}";
        using var resp = await _http.PostAsync(url, null, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("error", out var err) && err.GetInt32() != 0)
        {
            // session expired → reset and retry once
            _sid = null;
            sid = await EnsureSidAsync(ct);
            url = $"wialon/ajax.html?svc={svc}&params={HttpUtility.UrlEncode(paramsJson)}&sid={sid}";
            using var resp2 = await _http.PostAsync(url, null, ct);
            resp2.EnsureSuccessStatusCode();
            body = await resp2.Content.ReadAsStringAsync(ct);
            doc.Dispose();
            doc = JsonDocument.Parse(body);
        }
        return doc;
    }

    public async Task<GpsTracePoint?> GetLatestAsync(string unitId, CancellationToken ct = default)
    {
        if (!long.TryParse(unitId, out var id)) return null;
        var @params = new
        {
            spec = new { itemsType = "avl_unit", propName = "sys_id", propValueMask = unitId, sortType = "sys_id" },
            force = 1,
            flags = 0x00000001 | 0x00000400, // base + last_message
            from = 0,
            to = 0
        };
        using var doc = await CallAsync("core/search_items", @params, ct);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0) return null;
        var item = items[0];
        if (!item.TryGetProperty("lmsg", out var lmsg)) return null;
        return ParsePoint(unitId, lmsg);
    }

    public async Task<IReadOnlyList<GpsTracePoint>> GetPositionsAsync(string unitId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        if (!long.TryParse(unitId, out var id)) return Array.Empty<GpsTracePoint>();
        var fromUnix = new DateTimeOffset(from, TimeSpan.Zero).ToUnixTimeSeconds();
        var toUnix = new DateTimeOffset(to, TimeSpan.Zero).ToUnixTimeSeconds();

        // 1) Open message loader for this unit
        var loadParams = new
        {
            itemId = id,
            timeFrom = fromUnix,
            timeTo = toUnix,
            flags = 0x0000, // raw position messages
            flagsMask = 0xFF00,
            loadCount = 0xFFFFFFFF
        };
        using var openDoc = await CallAsync("messages/load_interval", loadParams, ct);

        // 2) Read in chunks
        var results = new List<GpsTracePoint>();
        int total = 0;
        if (openDoc.RootElement.TryGetProperty("count", out var c)) total = c.GetInt32();
        const int chunk = 1000;
        for (int offset = 0; offset < total; offset += chunk)
        {
            var getParams = new { indexFrom = offset, indexTo = Math.Min(offset + chunk, total) - 1 };
            using var msgDoc = await CallAsync("messages/get_messages", getParams, ct);
            if (!msgDoc.RootElement.TryGetProperty("messages", out var msgs)) break;
            foreach (var m in msgs.EnumerateArray())
            {
                var pt = ParsePoint(unitId, m);
                if (pt != null) results.Add(pt);
            }
        }

        // 3) Close the message session
        await CallAsync("messages/unload", new { }, ct);

        return results;
    }

    private static GpsTracePoint? ParsePoint(string unitId, JsonElement m)
    {
        if (!m.TryGetProperty("pos", out var pos)) return null;
        double lat = pos.GetProperty("y").GetDouble();
        double lng = pos.GetProperty("x").GetDouble();
        double speed = pos.TryGetProperty("s", out var s) ? s.GetDouble() : 0;
        double heading = pos.TryGetProperty("c", out var h) ? h.GetDouble() : 0;
        long unix = m.TryGetProperty("t", out var t) ? t.GetInt64() : 0;
        bool? ignition = null;
        // p[].param 'ignition' present in some firmwares
        if (m.TryGetProperty("p", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            if (p.TryGetProperty("ignition", out var ig))
                ignition = ig.GetDouble() > 0;
            else if (p.TryGetProperty("io_239", out var io239))
                ignition = io239.GetDouble() > 0;
        }
        var recordedAt = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
        return new GpsTracePoint(unitId, lat, lng, speed, heading, ignition, recordedAt);
    }
}
