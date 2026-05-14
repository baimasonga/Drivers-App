using System.Security.Claims;
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
[Route("api/trips")]
public class TripsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly ReconciliationService _recon;

    public TripsController(AppDbContext db, AuditService audit, ReconciliationService recon)
    {
        _db = db; _audit = audit; _recon = recon;
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")!.Value);

    [HttpGet("my-assignments")]
    [Authorize(Roles = "Driver,Admin")]
    public async Task<IEnumerable<object>> MyAssignments()
    {
        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.UserId == CurrentUserId);
        if (driver == null) return Enumerable.Empty<object>();
        return await _db.TravelRequests
            .Where(t => t.AssignedDriverId == driver.Id
                        && t.Status >= TripStatus.ApprovedForDispatch
                        && t.Status <= TripStatus.Returning)
            .Include(t => t.AssignedVehicle)
            .Include(t => t.DestinationGeofence)
            .Select(t => new
            {
                t.Id, t.RequestCode, Status = t.Status.ToString(),
                t.Purpose, t.PlannedDeparture, t.PlannedReturn,
                Vehicle = t.AssignedVehicle!.PlateNumber,
                Destination = t.DestinationGeofence != null ? t.DestinationGeofence.Name : t.SpecificDestination
            }).ToListAsync();
    }

    [HttpPost("gate-clearance")]
    [Authorize(Roles = "GateOfficer,Admin")]
    public async Task<IActionResult> GateClearance(GateClearanceRequest req)
    {
        var t = await _db.TravelRequests.FindAsync(req.TravelRequestId);
        if (t == null) return NotFound();

        if (req.Action == GateAction.Exit)
        {
            if (t.Status != TripStatus.ApprovedForDispatch && t.Status != TripStatus.ReadyForGateClearance)
                return BadRequest(new { error = $"Cannot exit in status {t.Status}" });
            t.Status = TripStatus.TripStarted;
            t.StartedAt = DateTime.UtcNow;
            t.StartOdometer = req.OdometerReading;
        }
        else
        {
            if (t.Status < TripStatus.TripStarted || t.Status >= TripStatus.ReturnedToBase)
                return BadRequest(new { error = $"Cannot register entry in status {t.Status}" });
            t.Status = TripStatus.ReturnedToBase;
            t.CompletedAt = DateTime.UtcNow;
            t.EndOdometer = req.OdometerReading;
        }

        _db.GateClearances.Add(new GateClearance
        {
            TravelRequestId = t.Id,
            Action = req.Action,
            GateOfficerId = CurrentUserId,
            OdometerReading = req.OdometerReading,
            GateLat = req.GateLat,
            GateLng = req.GateLng,
            Notes = req.Notes
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync($"Gate{req.Action}", "TravelRequest", t.Id);

        if (req.Action == GateAction.Entry)
        {
            // Trigger reconciliation asynchronously here in production. For MVP run inline.
            t.Status = TripStatus.PendingVerification;
            var result = await _recon.ReconcileAsync(t.Id);
            t.ComplianceScore = result.ComplianceScore;
            foreach (var ex in result.Exceptions) _db.TripExceptions.Add(ex);
            await _db.SaveChangesAsync();
        }

        return Ok(new { Status = t.Status.ToString() });
    }

    [HttpPost("log")]
    [Authorize(Roles = "Driver,Admin")]
    public async Task<IActionResult> Log(TripLogRequest req)
    {
        var t = await _db.TravelRequests.FindAsync(req.TravelRequestId);
        if (t == null) return NotFound();

        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.UserId == CurrentUserId);
        if (driver == null || t.AssignedDriverId != driver.Id)
            return Forbid();

        if (t.Status < TripStatus.TripStarted || t.Status >= TripStatus.ReturnedToBase)
            return BadRequest(new { error = "Trip not in active state" });

        _db.TripLogs.Add(new TripLog
        {
            TravelRequestId = t.Id,
            EventType = req.EventType,
            EventAt = req.EventAt ?? DateTime.UtcNow,
            PhoneLat = req.PhoneLat,
            PhoneLng = req.PhoneLng,
            Notes = req.Notes,
            OdometerReading = req.OdometerReading,
            LoggedByUserId = CurrentUserId,
            LocalUuid = req.LocalUuid,
            SyncedOffline = req.EventAt.HasValue && (DateTime.UtcNow - req.EventAt.Value).TotalMinutes > 5
        });

        // Soft status transitions based on event type
        switch (req.EventType)
        {
            case "Departed": if (t.Status == TripStatus.TripStarted) t.Status = TripStatus.InTransit; break;
            case "Arrived": t.Status = TripStatus.ArrivedAtDestination; break;
            case "Returning": t.Status = TripStatus.Returning; break;
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync("TripLog", "TravelRequest", t.Id, req.EventType);
        return Ok();
    }

    [HttpPost("{id:int}/verify")]
    [Authorize(Roles = "FleetOfficer,Admin")]
    public async Task<IActionResult> Verify(int id, [FromQuery] bool accept, [FromBody] string? note)
    {
        var t = await _db.TravelRequests.FindAsync(id);
        if (t == null) return NotFound();
        if (t.Status != TripStatus.PendingVerification)
            return BadRequest(new { error = "Not pending verification" });
        t.Status = accept ? TripStatus.Verified : TripStatus.Queried;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(accept ? "VerifyTrip" : "QueryTrip", "TravelRequest", t.Id, note);
        return Ok(new { Status = t.Status.ToString() });
    }

    [HttpPost("{id:int}/close")]
    [Authorize(Roles = "FleetOfficer,Manager,Admin")]
    public async Task<IActionResult> Close(int id)
    {
        var t = await _db.TravelRequests.FindAsync(id);
        if (t == null) return NotFound();
        if (t.Status != TripStatus.Verified && t.Status != TripStatus.Queried)
            return BadRequest(new { error = "Trip must be verified or queried before closing" });
        t.Status = TripStatus.Closed;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CloseTrip", "TravelRequest", t.Id);
        return Ok(new { Status = t.Status.ToString() });
    }
}
