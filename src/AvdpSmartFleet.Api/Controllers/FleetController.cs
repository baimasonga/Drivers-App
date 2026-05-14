using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using AvdpSmartFleet.Api.Dtos;
using AvdpSmartFleet.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class FleetController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    public FleetController(AppDbContext db, AuditService audit) { _db = db; _audit = audit; }

    [HttpGet("vehicles")]
    public async Task<IEnumerable<object>> Vehicles() =>
        await _db.Vehicles.Include(v => v.GpsUnit).Select(v => new
        {
            v.Id, v.PlateNumber, v.Make, v.Model, v.Type, Status = v.Status.ToString(),
            v.HomeBase, GpsUnit = v.GpsUnit != null ? v.GpsUnit.ExternalUnitId : null
        }).ToListAsync();

    [HttpPost("vehicles")]
    [Authorize(Roles = "FleetOfficer,Admin")]
    public async Task<ActionResult<object>> CreateVehicle(CreateVehicleRequest req)
    {
        if (await _db.Vehicles.AnyAsync(v => v.PlateNumber == req.PlateNumber))
            return BadRequest(new { error = "Plate exists" });
        var v = new Vehicle
        {
            PlateNumber = req.PlateNumber,
            Make = req.Make,
            Model = req.Model,
            Type = req.Type,
            HomeBase = req.HomeBase,
            GpsUnitId = req.GpsUnitId
        };
        _db.Vehicles.Add(v);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CreateVehicle", "Vehicle", v.Id, req.PlateNumber);
        return new { v.Id, v.PlateNumber };
    }

    [HttpGet("drivers")]
    public async Task<IEnumerable<object>> Drivers() =>
        await _db.Drivers.Include(d => d.User).Select(d => new
        {
            d.Id, Name = d.User.FullName, d.User.Phone, d.User.Email,
            d.LicenseNumber, d.IsAvailable
        }).ToListAsync();

    [HttpGet("geofences")]
    public async Task<IEnumerable<object>> Geofences() =>
        await _db.Geofences.Select(g => new
        {
            g.Id, g.Name, g.Category, g.CenterLat, g.CenterLng, g.RadiusMeters, g.IsProvisional
        }).ToListAsync();

    [HttpPost("geofences")]
    [Authorize(Roles = "FleetOfficer,Admin")]
    public async Task<ActionResult<object>> CreateGeofence(CreateGeofenceRequest req)
    {
        var g = new Geofence
        {
            Name = req.Name, Category = req.Category,
            CenterLat = req.CenterLat, CenterLng = req.CenterLng,
            RadiusMeters = req.RadiusMeters
        };
        _db.Geofences.Add(g);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CreateGeofence", "Geofence", g.Id, req.Name);
        return new { g.Id, g.Name };
    }

    [HttpGet("gps-units")]
    public async Task<IEnumerable<object>> GpsUnits() =>
        await _db.GpsUnits.Select(u => new { u.Id, u.ExternalUnitId, u.Imei, u.IsOnline, u.LastSeenAt }).ToListAsync();

    [HttpPost("gps-units")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<object>> CreateGpsUnit(CreateGpsUnitRequest req)
    {
        if (await _db.GpsUnits.AnyAsync(u => u.ExternalUnitId == req.ExternalUnitId))
            return BadRequest(new { error = "Unit exists" });
        var u = new GpsUnit { ExternalUnitId = req.ExternalUnitId, Imei = req.Imei };
        _db.GpsUnits.Add(u);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CreateGpsUnit", "GpsUnit", u.Id, req.ExternalUnitId);
        return new { u.Id, u.ExternalUnitId };
    }

    [HttpGet("exceptions")]
    [Authorize(Roles = "FleetOfficer,Manager,Auditor,Admin")]
    public async Task<IEnumerable<object>> Exceptions([FromQuery] ExceptionStatus? status)
    {
        var q = _db.TripExceptions.Include(e => e.TravelRequest).AsQueryable();
        if (status.HasValue) q = q.Where(e => e.Status == status.Value);
        return await q.OrderByDescending(e => e.DetectedAt).Take(200)
            .Select(e => new
            {
                e.Id, Type = e.Type.ToString(), Status = e.Status.ToString(),
                e.Description, e.DetectedAt,
                TripCode = e.TravelRequest.RequestCode
            }).ToListAsync();
    }

    [HttpGet("dashboard/kpis")]
    [Authorize(Roles = "Manager,Auditor,Admin,FleetOfficer")]
    public async Task<object> Kpis()
    {
        var from = DateTime.UtcNow.AddDays(-30);
        return new
        {
            totalRequests = await _db.TravelRequests.CountAsync(t => t.CreatedAt >= from),
            approved = await _db.TravelRequests.CountAsync(t => t.CreatedAt >= from && t.ApprovedAt != null),
            completed = await _db.TravelRequests.CountAsync(t => t.CompletedAt >= from),
            openExceptions = await _db.TripExceptions.CountAsync(e => e.Status == ExceptionStatus.Open),
            unauthorizedMovements = await _db.UnauthorizedMovements.CountAsync(u => !u.Resolved),
            avgCompliance = await _db.TravelRequests
                .Where(t => t.CompletedAt >= from && t.ComplianceScore != null)
                .AverageAsync(t => (double?)t.ComplianceScore) ?? 0
        };
    }
}
