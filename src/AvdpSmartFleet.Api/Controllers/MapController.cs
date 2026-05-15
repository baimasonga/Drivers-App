using AvdpSmartFleet.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/map")]
public class MapController : ControllerBase
{
    private readonly AppDbContext _db;
    public MapController(AppDbContext db) => _db = db;

    [HttpGet("live")]
    [AllowAnonymous]
    public async Task<object> LiveFleet()
    {
        // Latest position per vehicle currently on an active trip
        var activeTrips = await _db.TravelRequests
            .Include(t => t.AssignedVehicle).ThenInclude(v => v!.GpsUnit)
            .Include(t => t.AssignedDriver).ThenInclude(d => d!.User)
            .Include(t => t.DestinationGeofence)
            .Where(t => t.Status >= Domain.TripStatus.TripStarted
                        && t.Status <= Domain.TripStatus.Returning)
            .ToListAsync();

        var vehicles = new List<object>();
        foreach (var t in activeTrips)
        {
            if (t.AssignedVehicle?.GpsUnitId == null) continue;
            var latest = await _db.GpsPositions
                .Where(p => p.GpsUnitId == t.AssignedVehicle.GpsUnitId)
                .OrderByDescending(p => p.RecordedAt)
                .FirstOrDefaultAsync();
            // Fallback to last driver phone log if no GPS-Trace position yet
            double? lat = latest?.Lat;
            double? lng = latest?.Lng;
            DateTime? at = latest?.RecordedAt;
            string source = "tracker";
            if (lat == null)
            {
                var lastLog = await _db.TripLogs
                    .Where(l => l.TravelRequestId == t.Id && l.PhoneLat != null)
                    .OrderByDescending(l => l.EventAt)
                    .FirstOrDefaultAsync();
                if (lastLog != null)
                {
                    lat = lastLog.PhoneLat; lng = lastLog.PhoneLng;
                    at = lastLog.EventAt; source = "phone";
                }
            }
            if (lat == null) continue;

            bool isStale = at.HasValue && (DateTime.UtcNow - at.Value).TotalMinutes > 10;
            bool hasOpenException = await _db.TripExceptions
                .AnyAsync(e => e.TravelRequestId == t.Id && e.Status == Domain.ExceptionStatus.Open);

            vehicles.Add(new
            {
                tripId = t.Id,
                tripCode = t.RequestCode,
                plate = t.AssignedVehicle.PlateNumber,
                driver = t.AssignedDriver?.User.FullName,
                destination = t.DestinationGeofence?.Name,
                status = t.Status.ToString(),
                lat,
                lng,
                speed = latest?.SpeedKph,
                source,
                isStale,
                hasOpenException,
                lastAt = at
            });
        }
        return new { count = vehicles.Count, vehicles, generatedAt = DateTime.UtcNow };
    }

    [HttpGet("trip/{id:int}/positions")]
    [AllowAnonymous] // Public for embedded map view; tighten in production
    public async Task<object> TripPositions(int id)
    {
        var trip = await _db.TravelRequests
            .Include(t => t.AssignedVehicle).ThenInclude(v => v!.GpsUnit)
            .Include(t => t.DestinationGeofence)
            .Include(t => t.TripLogs)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (trip == null) return new { error = "Trip not found" };

        var positions = await _db.GpsPositions
            .Where(p => p.TravelRequestId == id)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new { lat = p.Lat, lng = p.Lng, t = p.RecordedAt, speed = p.SpeedKph })
            .ToListAsync();

        return new
        {
            code = trip.RequestCode,
            destination = trip.DestinationGeofence == null ? null : new
            {
                trip.DestinationGeofence.Name,
                lat = trip.DestinationGeofence.CenterLat,
                lng = trip.DestinationGeofence.CenterLng,
                radius = trip.DestinationGeofence.RadiusMeters
            },
            driverLogs = trip.TripLogs
                .Where(l => l.PhoneLat.HasValue && l.PhoneLng.HasValue)
                .Select(l => new { lat = l.PhoneLat, lng = l.PhoneLng, l.EventType, l.EventAt }),
            positions
        };
    }
}
