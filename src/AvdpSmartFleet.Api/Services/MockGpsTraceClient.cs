namespace AvdpSmartFleet.Api.Services;

/// <summary>
/// Mock GPS-Trace client used until real API credentials are available.
/// Simulates a straight-line journey between Freetown HQ and the requested destination.
/// Swap with a real HTTP client implementing IGpsTraceClient in Program.cs.
/// </summary>
public class MockGpsTraceClient : IGpsTraceClient
{
    // Freetown HQ default
    private const double BaseLat = 8.4844;
    private const double BaseLng = -13.2344;

    public Task<GpsTracePoint?> GetLatestAsync(string unitId, CancellationToken ct = default)
    {
        var pt = new GpsTracePoint(unitId, BaseLat, BaseLng, 0, 0, false, DateTime.UtcNow);
        return Task.FromResult<GpsTracePoint?>(pt);
    }

    public Task<IReadOnlyList<GpsTracePoint>> GetPositionsAsync(string unitId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var points = new List<GpsTracePoint>();
        var duration = to - from;
        if (duration <= TimeSpan.Zero) return Task.FromResult<IReadOnlyList<GpsTracePoint>>(points);

        // Synthesise a trip from base toward Kambia (approx 9.1267, -12.9181) and back.
        var destLat = 9.1267;
        var destLng = -12.9181;
        int steps = Math.Max(10, (int)duration.TotalMinutes);
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            double frac = t < 0.5 ? t * 2 : (1 - (t - 0.5) * 2);
            double lat = BaseLat + (destLat - BaseLat) * frac;
            double lng = BaseLng + (destLng - BaseLng) * frac;
            double speed = (frac > 0.05 && frac < 0.95) ? 55 : 0;
            points.Add(new GpsTracePoint(
                unitId, lat, lng, speed, 90,
                speed > 0,
                from.AddMinutes(i * duration.TotalMinutes / steps)));
        }
        return Task.FromResult<IReadOnlyList<GpsTracePoint>>(points);
    }
}
