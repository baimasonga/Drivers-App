using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Services;

public class ReconciliationResult
{
    public double ComplianceScore { get; set; }
    public List<TripException> Exceptions { get; set; } = new();
    public double DistanceKm { get; set; }
    public bool DestinationReached { get; set; }
}

public class ReconciliationService
{
    private readonly AppDbContext _db;
    private readonly IGpsTraceClient _gps;

    public ReconciliationService(AppDbContext db, IGpsTraceClient gps)
    {
        _db = db;
        _gps = gps;
    }

    public async Task<ReconciliationResult> ReconcileAsync(int travelRequestId, CancellationToken ct = default)
    {
        var trip = await _db.TravelRequests
            .Include(t => t.AssignedVehicle).ThenInclude(v => v!.GpsUnit)
            .Include(t => t.DestinationGeofence)
            .Include(t => t.TripLogs)
            .FirstOrDefaultAsync(t => t.Id == travelRequestId, ct)
            ?? throw new InvalidOperationException("Trip not found");

        var result = new ReconciliationResult();
        if (trip.AssignedVehicle?.GpsUnit == null)
        {
            result.Exceptions.Add(new TripException
            {
                TravelRequestId = trip.Id,
                Type = ExceptionType.TrackerOffline,
                Description = "No GPS unit linked to assigned vehicle"
            });
            return result;
        }

        var from = trip.StartedAt ?? trip.PlannedDeparture;
        var to = trip.CompletedAt ?? trip.PlannedReturn;
        var positions = await _gps.GetPositionsAsync(trip.AssignedVehicle.GpsUnit.ExternalUnitId, from, to, ct);

        // Distance
        double totalMeters = 0;
        for (int i = 1; i < positions.Count; i++)
            totalMeters += GeoUtils.HaversineMeters(
                positions[i - 1].Lat, positions[i - 1].Lng,
                positions[i].Lat, positions[i].Lng);
        result.DistanceKm = totalMeters / 1000.0;

        // Destination check
        double destinationScore = 0;
        if (trip.DestinationGeofence != null)
        {
            var dest = trip.DestinationGeofence;
            result.DestinationReached = positions.Any(p =>
                GeoUtils.HaversineMeters(p.Lat, p.Lng, dest.CenterLat, dest.CenterLng) <= dest.RadiusMeters);
            if (result.DestinationReached) destinationScore = 30;
            else
                result.Exceptions.Add(new TripException
                {
                    TravelRequestId = trip.Id,
                    Type = ExceptionType.DestinationMismatch,
                    Description = $"Vehicle did not enter the {dest.RadiusMeters}m geofence at {dest.Name}"
                });
        }

        // Time compliance
        double timeScore = 15;
        if (trip.CompletedAt.HasValue && trip.CompletedAt > trip.PlannedReturn.AddMinutes(60))
        {
            timeScore = 5;
            result.Exceptions.Add(new TripException
            {
                TravelRequestId = trip.Id,
                Type = ExceptionType.LateReturn,
                Description = $"Returned {(trip.CompletedAt.Value - trip.PlannedReturn).TotalMinutes:F0} min late"
            });
        }

        // Odometer check
        double odoScore = 10;
        if (trip.StartOdometer.HasValue && trip.EndOdometer.HasValue)
        {
            var claimedKm = trip.EndOdometer.Value - trip.StartOdometer.Value;
            var deltaPct = result.DistanceKm > 0
                ? Math.Abs(claimedKm - result.DistanceKm) / result.DistanceKm
                : 0;
            if (deltaPct > 0.10)
            {
                odoScore = 3;
                result.Exceptions.Add(new TripException
                {
                    TravelRequestId = trip.Id,
                    Type = ExceptionType.OdometerMismatch,
                    Description = $"Odometer reports {claimedKm} km, GPS distance {result.DistanceKm:F1} km"
                });
            }
        }

        // Route + stops (simplified): unauthorized stop = stationary > 10 min outside any geofence
        double routeScore = 20;
        double stopsScore = 15;
        var geofences = await _db.Geofences.ToListAsync(ct);
        int stationaryMinutes = 0;
        for (int i = 1; i < positions.Count; i++)
        {
            if (positions[i].SpeedKph < 2)
            {
                stationaryMinutes++;
                if (stationaryMinutes == 11)
                {
                    bool insideAny = geofences.Any(g =>
                        GeoUtils.HaversineMeters(positions[i].Lat, positions[i].Lng, g.CenterLat, g.CenterLng) <= g.RadiusMeters);
                    if (!insideAny)
                    {
                        stopsScore = 5;
                        result.Exceptions.Add(new TripException
                        {
                            TravelRequestId = trip.Id,
                            Type = ExceptionType.UnauthorizedStop,
                            Description = $"Stop >10 min outside any geofence at {positions[i].Lat:F4},{positions[i].Lng:F4}"
                        });
                    }
                }
            }
            else stationaryMinutes = 0;
        }

        // Driver log completeness
        double logsScore = trip.TripLogs.Count >= 3 ? 10 : 5;

        result.ComplianceScore = destinationScore + routeScore + timeScore + stopsScore + odoScore + logsScore;
        return result;
    }
}
