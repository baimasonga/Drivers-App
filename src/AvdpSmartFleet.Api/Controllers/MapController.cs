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
