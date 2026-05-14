using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Services;

/// <summary>
/// Background hosted service that periodically polls GPS-Trace for vehicles with active trips,
/// persists positions, and detects unauthorized movement when no active trip covers the vehicle.
/// </summary>
public class GpsPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<GpsPollingService> _log;
    private readonly TimeSpan _activeInterval = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _idleInterval = TimeSpan.FromMinutes(15);

    public GpsPollingService(IServiceScopeFactory scopes, ILogger<GpsPollingService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Stagger startup so we don't hit the DB before seeding finishes
        await Task.Delay(TimeSpan.FromSeconds(10), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "GPS polling iteration failed");
            }
            await Task.Delay(_activeInterval, ct);
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gps = scope.ServiceProvider.GetRequiredService<IGpsTraceClient>();

        var vehicles = await db.Vehicles.Include(v => v.GpsUnit).Where(v => v.GpsUnitId != null).ToListAsync(ct);
        foreach (var v in vehicles)
        {
            var unit = v.GpsUnit!;
            var latest = await gps.GetLatestAsync(unit.ExternalUnitId, ct);
            if (latest == null) continue;

            unit.LastSeenAt = latest.RecordedAt;
            unit.IsOnline = (DateTime.UtcNow - latest.RecordedAt).TotalMinutes < 10;

            // Find an active trip covering this vehicle right now
            var activeTrip = await db.TravelRequests
                .Where(t => t.AssignedVehicleId == v.Id
                            && t.Status >= TripStatus.TripStarted
                            && t.Status <= TripStatus.Returning)
                .FirstOrDefaultAsync(ct);

            db.GpsPositions.Add(new GpsPosition
            {
                GpsUnitId = unit.Id,
                TravelRequestId = activeTrip?.Id,
                Lat = latest.Lat,
                Lng = latest.Lng,
                SpeedKph = latest.SpeedKph,
                Heading = latest.Heading,
                IgnitionOn = latest.IgnitionOn,
                RecordedAt = latest.RecordedAt
            });

            // Unauthorized movement: vehicle moving (>5 kph) without an active trip
            if (activeTrip == null && latest.SpeedKph > 5)
            {
                bool alreadyOpen = await db.UnauthorizedMovements
                    .AnyAsync(u => u.VehicleId == v.Id && !u.Resolved
                                   && u.DetectedAt > DateTime.UtcNow.AddHours(-1), ct);
                if (!alreadyOpen)
                {
                    db.UnauthorizedMovements.Add(new UnauthorizedMovement
                    {
                        VehicleId = v.Id,
                        Lat = latest.Lat,
                        Lng = latest.Lng
                    });
                    _log.LogWarning("Unauthorized movement detected for vehicle {Plate}", v.PlateNumber);
                }
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
