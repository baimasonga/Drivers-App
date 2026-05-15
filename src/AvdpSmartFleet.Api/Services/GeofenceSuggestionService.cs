using AvdpSmartFleet.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Services;

public record GeofenceSuggestion(double Lat, double Lng, int StopCount, double RadiusMeters, List<string> SampleTrips);

/// <summary>
/// Analyses recent GPS positions where vehicles were stationary >10 minutes,
/// clusters them via a simple grid-bucketing approach (no external ML), and
/// suggests new geofences for locations the fleet visits often but that aren't
/// registered yet.
/// </summary>
public class GeofenceSuggestionService
{
    private readonly AppDbContext _db;
    public GeofenceSuggestionService(AppDbContext db) => _db = db;

    public async Task<List<GeofenceSuggestion>> SuggestAsync(int minStops = 3, int days = 60, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow.AddDays(-days);

        // Pull positions where speed is near zero — these are stops
        var stops = await _db.GpsPositions
            .Where(p => p.RecordedAt >= from && (p.SpeedKph ?? 0) < 2)
            .Select(p => new { p.Lat, p.Lng, p.TravelRequestId })
            .ToListAsync(ct);

        // Existing geofences — we won't suggest near these
        var existing = await _db.Geofences
            .Select(g => new { g.CenterLat, g.CenterLng, g.RadiusMeters })
            .ToListAsync(ct);
        bool NearExisting(double lat, double lng) =>
            existing.Any(g => GeoUtils.HaversineMeters(lat, lng, g.CenterLat, g.CenterLng) <= g.RadiusMeters + 200);

        // Bucket by ~110m × 110m grid (0.001° lat ≈ 111m)
        var grid = stops
            .Where(s => !NearExisting(s.Lat, s.Lng))
            .GroupBy(s => (Math.Round(s.Lat, 3), Math.Round(s.Lng, 3)))
            .Where(g => g.Count() >= minStops)
            .OrderByDescending(g => g.Count())
            .Take(15);

        var trips = await _db.TravelRequests.ToDictionaryAsync(t => t.Id, t => t.RequestCode, ct);

        return grid.Select(g =>
        {
            var centreLat = g.Average(x => x.Lat);
            var centreLng = g.Average(x => x.Lng);
            var maxDist = g.Max(x => GeoUtils.HaversineMeters(centreLat, centreLng, x.Lat, x.Lng));
            var radius = Math.Max(100, Math.Min(500, maxDist + 50));
            var sampleTrips = g.Select(x => x.TravelRequestId).Where(id => id.HasValue)
                .Select(id => trips.GetValueOrDefault(id!.Value) ?? "")
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct().Take(5).ToList();
            return new GeofenceSuggestion(centreLat, centreLng, g.Count(), Math.Round(radius), sampleTrips);
        }).ToList();
    }
}
