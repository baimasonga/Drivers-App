using System.Security.Claims;
using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using AvdpSmartFleet.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Controllers;

public record LogServiceRequest(
    ServiceType Type,
    int? OdometerAt,
    DateTime? PerformedAt,
    string? Notes,
    decimal? CostSll,
    string? ServicedBy);

[ApiController]
[Authorize]
[Route("api/maintenance")]
public class MaintenanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly VehicleHealthService _health;

    public MaintenanceController(AppDbContext db, AuditService audit, VehicleHealthService health)
    {
        _db = db; _audit = audit; _health = health;
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")!.Value);

    [HttpGet("health")]
    [Authorize(Roles = "FleetOfficer,Manager,Auditor,Admin")]
    public async Task<IEnumerable<VehicleHealth>> Health() => await _health.GetAllAsync();

    [HttpGet("vehicles/{id:int}/health")]
    [Authorize(Roles = "FleetOfficer,Manager,Auditor,Admin")]
    public async Task<ActionResult<VehicleHealth>> Vehicle(int id)
    {
        var h = await _health.GetForVehicleAsync(id);
        return h == null ? NotFound() : h;
    }

    [HttpGet("vehicles/{id:int}/services")]
    [Authorize(Roles = "FleetOfficer,Manager,Auditor,Admin")]
    public async Task<IEnumerable<object>> Services(int id)
    {
        return await _db.ServiceRecords
            .Where(s => s.VehicleId == id)
            .OrderByDescending(s => s.PerformedAt)
            .Select(s => new
            {
                s.Id, type = s.Type.ToString(), s.OdometerAt, s.PerformedAt,
                s.Notes, s.CostSll, s.ServicedBy
            })
            .ToListAsync();
    }

    [HttpPost("vehicles/{id:int}/services")]
    [Authorize(Roles = "FleetOfficer,Admin")]
    public async Task<ActionResult<object>> Log(int id, LogServiceRequest req)
    {
        var v = await _db.Vehicles.FindAsync(id);
        if (v == null) return NotFound();

        var s = new ServiceRecord
        {
            VehicleId = id,
            Type = req.Type,
            OdometerAt = req.OdometerAt,
            PerformedAt = req.PerformedAt ?? DateTime.UtcNow,
            Notes = req.Notes,
            CostSll = req.CostSll,
            ServicedBy = req.ServicedBy,
            RecordedByUserId = CurrentUserId
        };
        _db.ServiceRecords.Add(s);

        // Reset service counter when this is a service that resets the interval
        if (req.Type is ServiceType.OilChange or ServiceType.FullService or ServiceType.BrakeService)
        {
            v.LastServiceOdometer = req.OdometerAt ?? v.CurrentOdometer;
            v.LastServiceAt = s.PerformedAt;
        }
        // Capture current odometer if newer
        if (req.OdometerAt.HasValue && (v.CurrentOdometer == null || req.OdometerAt > v.CurrentOdometer))
            v.CurrentOdometer = req.OdometerAt;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("LogService", "Vehicle", id, $"{req.Type}@{req.OdometerAt}km");
        return new { s.Id };
    }
}
